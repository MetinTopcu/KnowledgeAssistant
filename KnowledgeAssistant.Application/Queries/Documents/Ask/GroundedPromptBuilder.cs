using System.Globalization;
using System.Text;
using KnowledgeAssistant.Application.Interfaces;

namespace KnowledgeAssistant.Application.Queries.Documents.Ask;

/// <summary>
/// Turns a question and its retrieved evidence into chat messages.
/// </summary>
/// <remarks>
/// <para>
/// <b>A pure function, and deliberately so.</b> No I/O, no clock, no injected
/// dependency — the same question and chunks always produce the same messages.
/// The prompt is the single largest determinant of answer quality, and this makes
/// it something that can be read in one file, diffed in a review, and asserted on
/// in a test without a model in the loop.
/// </para>
/// <para>
/// <b>It lives in Application, not Infrastructure.</b> The prompt encodes what
/// this product considers a good answer: ground everything, cite sources, decline
/// when the evidence is absent. Those are product rules that must survive
/// changing the model behind them, so they belong with the use case rather than
/// with the vendor adapter.
/// </para>
/// <para>
/// <b>What the instructions are actually defending against.</b> A retrieval
/// pipeline can only fail in two visible ways: the model answers from its own
/// training rather than the sources, or it declines when the answer is present.
/// The system message below is aimed squarely at the first, because a confident
/// ungrounded answer is indistinguishable from a correct one to the reader who
/// needed to ask.
/// </para>
/// </remarks>
internal static class GroundedPromptBuilder
{
    /// <summary>
    /// The upper bound on characters of retrieved text placed in one prompt.
    /// </summary>
    /// <remarks>
    /// A backstop, not the usual limiter — the requested chunk count normally
    /// binds first. It exists because chunk size is configurable and the chunk
    /// count is caller-supplied, so their product is not something this code can
    /// assume stays small. Exceeding the model's context window fails the whole
    /// request, and dropping the least relevant chunks degrades the answer
    /// instead.
    /// </remarks>
    internal const int MaxContextCharacters = 24_000;

    /// <summary>The instruction message sent with every question.</summary>
    internal const string SystemPrompt =
        """
        You are a knowledge assistant. Answer the user's question using ONLY the numbered sources provided.

        Rules:
        - Base every statement on the sources. Do not use prior knowledge, and do not infer beyond what they say.
        - Cite the sources you used inline, as [1], [2], and so on. Cite every claim.
        - If the sources do not contain enough information to answer, say so plainly and state what is missing. Do not guess.
        - If the sources disagree, say that they disagree and cite each side.
        - Answer in the language of the question.
        - Be concise. Do not repeat the question or describe what you are about to do.
        """;

    /// <summary>The answer returned when retrieval found nothing.</summary>
    /// <remarks>
    /// Returned without calling the model at all. With no sources, the system
    /// message would leave it nothing to do but decline — so paying for a
    /// completion to be told that, and giving an ungrounded answer the
    /// opportunity to appear, are both avoidable.
    /// </remarks>
    internal const string NoEvidenceAnswer =
        "I could not find anything in the indexed documents that addresses this question.";

    /// <summary>
    /// Builds the messages for a question and the chunks retrieved for it.
    /// </summary>
    /// <param name="question">The user's question.</param>
    /// <param name="chunks">The retrieved chunks, in rank order.</param>
    /// <returns>A system message followed by one user message.</returns>
    internal static IReadOnlyList<ChatMessage> Build(
        string question,
        IReadOnlyList<ChunkSearchResult> chunks)
    {
        var user = new StringBuilder();

        user.AppendLine("Sources:");
        user.AppendLine();

        int usedCharacters = 0;

        for (int index = 0; index < chunks.Count; index++)
        {
            string text = chunks[index].Text;

            // Chunks arrive in rank order, so stopping here drops the least
            // relevant evidence rather than an arbitrary slice of it.
            if (usedCharacters + text.Length > MaxContextCharacters)
            {
                break;
            }

            usedCharacters += text.Length;

            // The reference number is the citation contract: it is what the model
            // is told to cite and what the response's citations are numbered with,
            // so the two cannot disagree.
            user.Append('[').Append((index + 1).ToString(CultureInfo.InvariantCulture)).AppendLine("]");
            user.AppendLine(text);
            user.AppendLine();
        }

        user.AppendLine("Question:");
        user.Append(question);

        return
        [
            new ChatMessage(ChatRole.System, SystemPrompt),
            new ChatMessage(ChatRole.User, user.ToString()),
        ];
    }

    /// <summary>
    /// How many of <paramref name="chunks"/> fit inside the context budget.
    /// </summary>
    /// <remarks>
    /// Exposed so the caller can number its citations to match the prompt exactly.
    /// A citation list longer than the evidence actually sent would attribute the
    /// answer to text the model never saw.
    /// </remarks>
    internal static int CountChunksThatFit(IReadOnlyList<ChunkSearchResult> chunks)
    {
        int usedCharacters = 0;

        for (int index = 0; index < chunks.Count; index++)
        {
            int length = chunks[index].Text.Length;

            if (usedCharacters + length > MaxContextCharacters)
            {
                return index;
            }

            usedCharacters += length;
        }

        return chunks.Count;
    }
}
