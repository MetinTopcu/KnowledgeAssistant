using System.Text.RegularExpressions;

namespace KnowledgeAssistant.Infrastructure.Search.Chunking;

/// <summary>
/// Splits text into overlapping chunks that respect paragraph and word
/// boundaries.
/// </summary>
/// <remarks>
/// <para>
/// <b>A pure function, and deliberately so.</b> No I/O, no clock, no injected
/// dependency, no state between calls — the same input always produces the same
/// output. That is what makes the interesting cases here (a paragraph longer
/// than a chunk, a token longer than a chunk, text of exactly the boundary
/// length) testable directly, with a string literal and an assertion, rather
/// than through a PDF fixture and a mock.
/// </para>
/// <para>
/// <b>The rules, in priority order.</b> When a chunk must end:
/// </para>
/// <list type="number">
/// <item>
/// End at a paragraph break, if one falls late enough in the window to leave a
/// chunk worth emitting.
/// </item>
/// <item>
/// Otherwise end at the last word boundary in the window, so no word is ever cut
/// in half.
/// </item>
/// <item>
/// Otherwise cut at the size limit. This is reachable only when a single
/// unbroken token is longer than an entire chunk — a base64 blob or a long URL —
/// where honouring rule 2 is arithmetically impossible.
/// </item>
/// </list>
/// <para>
/// <b>Termination is a correctness property, not an assumption.</b> Each
/// iteration advances the read position by at least one character, and the
/// position is bounded by the length of the text, so the loop always terminates.
/// The two places that could violate this — an overlap larger than the chunk
/// just emitted, and an overlap that snaps forward past the end of that chunk —
/// are both clamped explicitly rather than being argued to be impossible.
/// </para>
/// </remarks>
internal static partial class TextChunker
{
    /// <summary>
    /// The fraction of the maximum size a chunk must reach before a paragraph
    /// break is preferred over a word boundary.
    /// </summary>
    /// <remarks>
    /// Without a floor, "prefer paragraph breaks" degenerates. A window whose
    /// only paragraph break sits ten characters in would emit a ten-character
    /// chunk and then re-read almost the same text — technically respecting
    /// paragraphs while producing a corpus of fragments. Requiring a paragraph
    /// break to fall in the second half of the window keeps the guarantee
    /// meaningful and the chunks useful.
    /// </remarks>
    private const double ParagraphBreakMinimumFill = 0.5;

    /// <summary>
    /// Splits <paramref name="text"/> into chunks of at most
    /// <paramref name="maxChunkSize"/> characters, each repeating
    /// <paramref name="overlapSize"/> characters of the previous chunk.
    /// </summary>
    /// <param name="text">The text to split. May be empty.</param>
    /// <param name="maxChunkSize">The maximum characters per chunk.</param>
    /// <param name="overlapSize">
    /// The characters of context to repeat between consecutive chunks. Must be
    /// smaller than <paramref name="maxChunkSize"/>.
    /// </param>
    /// <returns>
    /// The chunks in reading order, or an empty list if the text contains no
    /// non-whitespace characters.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="maxChunkSize"/> is not positive, or
    /// <paramref name="overlapSize"/> is negative or not smaller than
    /// <paramref name="maxChunkSize"/>.
    /// </exception>
    /// <remarks>
    /// The guard clauses throw rather than returning a failure result: these are
    /// not runtime conditions but programming errors in the calling code, and
    /// configuration validation already rejects them before startup completes.
    /// </remarks>
    internal static IReadOnlyList<string> Split(string text, int maxChunkSize, int overlapSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxChunkSize);
        ArgumentOutOfRangeException.ThrowIfNegative(overlapSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(overlapSize, maxChunkSize);

        string normalized = Normalize(text);

        if (normalized.Length == 0)
        {
            return [];
        }

        if (normalized.Length <= maxChunkSize)
        {
            return [normalized];
        }

        var chunks = new List<string>();
        int start = 0;

        while (start < normalized.Length)
        {
            // Step over leading whitespace before the window is measured. It would
            // otherwise spend part of the chunk's character budget on characters
            // the trim below immediately discards — which is not merely wasteful:
            // a word that would have fit exactly gets split instead, because the
            // budget ran out by however many spaces the chunk opened with.
            while (start < normalized.Length && char.IsWhiteSpace(normalized[start]))
            {
                start++;
            }

            if (start >= normalized.Length)
            {
                break;
            }

            int limit = Math.Min(start + maxChunkSize, normalized.Length);

            // The tail of the text needs no break-point search: it already fits.
            int end = limit == normalized.Length
                ? limit
                : FindBreakPoint(normalized, start, limit, maxChunkSize);

            string chunk = normalized[start..end].Trim();

            if (chunk.Length > 0)
            {
                chunks.Add(chunk);
            }

            if (end >= normalized.Length)
            {
                break;
            }

            start = FindNextStart(normalized, start, end, overlapSize);
        }

        return chunks;
    }

    /// <summary>
    /// Chooses where the chunk beginning at <paramref name="start"/> should end.
    /// </summary>
    /// <returns>
    /// An exclusive end index, always greater than <paramref name="start"/>.
    /// </returns>
    private static int FindBreakPoint(string text, int start, int limit, int maxChunkSize)
    {
        ReadOnlySpan<char> window = text.AsSpan(start, limit - start);

        // Rule 1: the last paragraph break in the window, if it leaves enough text
        // behind it to be worth emitting as a chunk.
        int minimumEnd = start + (int)(maxChunkSize * ParagraphBreakMinimumFill);
        int paragraphBreak = window.LastIndexOf("\n\n".AsSpan());

        if (paragraphBreak > 0 && start + paragraphBreak >= minimumEnd)
        {
            return start + paragraphBreak;
        }

        // Rule 2: the last word boundary in the window. No minimum applies here —
        // a short chunk is a cosmetic problem, and splitting a word is a
        // correctness one, so the boundary wins wherever it falls.
        for (int index = limit - 1; index > start; index--)
        {
            if (char.IsWhiteSpace(text[index]))
            {
                return index;
            }
        }

        // Rule 3: one unbroken token longer than a whole chunk. Nothing can be
        // preserved, so cut at the limit and keep going.
        return limit;
    }

    /// <summary>
    /// Chooses where the next chunk should begin, applying the overlap.
    /// </summary>
    /// <returns>
    /// A start index strictly greater than the previous <paramref name="start"/>,
    /// which is what guarantees the caller's loop terminates.
    /// </returns>
    private static int FindNextStart(string text, int start, int end, int overlapSize)
    {
        // Clamped to start + 1 so that a chunk shorter than the overlap cannot
        // send the next chunk backwards, or leave it where it was.
        int candidate = Math.Max(end - overlapSize, start + 1);

        // Walk forward to the beginning of a whole word, so the overlap never
        // opens mid-word. A position is a word start when the character before it
        // is whitespace.
        while (candidate < end && !char.IsWhiteSpace(text[candidate - 1]))
        {
            candidate++;
        }

        // Then step over the whitespace itself, so the chunk does not open with it.
        while (candidate < end && char.IsWhiteSpace(text[candidate]))
        {
            candidate++;
        }

        // Reaching end means the window held no word boundary to align to — the
        // long-token case. The next chunk simply starts where this one stopped,
        // forfeiting the overlap rather than the guarantee of progress.
        return candidate > start ? candidate : end;
    }

    /// <summary>
    /// Puts text into a canonical form before it is measured or split.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Normalisation happens first because every other rule depends on it. Chunk
    /// sizes are counted in characters, so a document using CRLF would otherwise
    /// yield chunks holding measurably less text than the same document using LF.
    /// Paragraph detection depends on a blank line being exactly <c>\n\n</c>, and
    /// extracted PDF text is full of trailing spaces and runs of blank lines that
    /// would otherwise defeat the match.
    /// </para>
    /// <para>
    /// What it does <b>not</b> do is collapse spaces inside a line or strip
    /// punctuation. Both would corrupt the text that is eventually shown to a
    /// user or fed to a model, for no benefit to the split.
    /// </para>
    /// </remarks>
    internal static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string unified = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        // Replaced with nothing, not with "\n": the pattern uses a lookahead, so
        // the line break it matches against is still in the text. Substituting a
        // newline here would append a second one and turn every line with a
        // trailing space into a paragraph break — which, on PDF-extracted text,
        // is very nearly every line.
        string trimmedLines = TrailingLineWhitespace().Replace(unified, string.Empty);

        return ExcessBlankLines().Replace(trimmedLines, "\n\n").Trim();
    }

    /// <summary>Matches spaces and tabs immediately before a line break.</summary>
    [GeneratedRegex(@"[ \t]+(?=\n)")]
    private static partial Regex TrailingLineWhitespace();

    /// <summary>Matches three or more consecutive line breaks.</summary>
    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExcessBlankLines();
}
