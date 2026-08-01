using System.Text;
using KnowledgeAssistant.Infrastructure.Search.Chunking;

namespace KnowledgeAssistant.Tests.Unit.Chunking;

/// <summary>
/// The chunking algorithm: sizing, word boundaries, paragraphs, and termination.
/// </summary>
/// <remarks>
/// A pure function, so every case here is a string literal and an assertion —
/// no fixture, no fake, no PDF. Two real bugs were found by this suite when it
/// was first written: a normalisation rule that manufactured paragraph breaks on
/// any line with a trailing space, and a chunk that spent part of its character
/// budget on leading whitespace and so split a word that would have fit.
/// </remarks>
public sealed class TextChunkerTests
{
    private const int MaxChunkSize = 800;
    private const int OverlapSize = 200;

    private static HashSet<string> Words(string value) =>
        new(value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);

    private static string Lorem(int wordCount) =>
        string.Join(" ", Enumerable.Range(0, wordCount).Select(index => $"word{index}"));

    [Theory]
    [InlineData("")]
    [InlineData("   \n\n \t ")]
    [InlineData("\r\n\r\n")]
    public void Split_WhenTextHasNoContent_ReturnsNoChunks(string text)
    {
        TextChunker.Split(text, MaxChunkSize, OverlapSize).Should().BeEmpty();
    }

    [Fact]
    public void Split_WhenTextFitsInOneChunk_ReturnsItTrimmed()
    {
        IReadOnlyList<string> chunks = TextChunker.Split("  hello world  ", MaxChunkSize, OverlapSize);

        chunks.Should().ContainSingle().Which.Should().Be("hello world");
    }

    [Fact]
    public void Split_WhenTextIsExactlyMaxChunkSize_ReturnsOneChunk()
    {
        string text = new string('a', 400) + " " + new string('b', 399);

        text.Length.Should().Be(MaxChunkSize);
        TextChunker.Split(text, MaxChunkSize, OverlapSize).Should().ContainSingle();
    }

    [Fact]
    public void Split_WhenTextIsLong_ProducesChunksWithinTheSizeLimit()
    {
        IReadOnlyList<string> chunks = TextChunker.Split(Lorem(900), MaxChunkSize, OverlapSize);

        chunks.Should().HaveCountGreaterThan(1);
        chunks.Should().OnlyContain(chunk => chunk.Length <= MaxChunkSize);
    }

    [Fact]
    public void Split_NeverSplitsAWordInHalf()
    {
        string text = Lorem(900);
        HashSet<string> source = Words(TextChunker.Normalize(text));

        IEnumerable<string> fragments = TextChunker
            .Split(text, MaxChunkSize, OverlapSize)
            .SelectMany(Words)
            .Where(word => !source.Contains(word));

        fragments.Should().BeEmpty("a word appearing in a chunk but not in the source is a fragment");
    }

    [Fact]
    public void Split_LosesNoText()
    {
        string text = Lorem(900);
        HashSet<string> source = Words(TextChunker.Normalize(text));

        HashSet<string> covered = [.. TextChunker
            .Split(text, MaxChunkSize, OverlapSize)
            .SelectMany(Words)];

        source.Should().BeSubsetOf(covered);
    }

    [Fact]
    public void Split_MakesConsecutiveChunksShareContext()
    {
        IReadOnlyList<string> chunks = TextChunker.Split(Lorem(900), MaxChunkSize, OverlapSize);

        for (int index = 1; index < chunks.Count; index++)
        {
            Words(chunks[index - 1]).Overlaps(Words(chunks[index]))
                .Should().BeTrue($"chunk {index} should repeat context from chunk {index - 1}");
        }
    }

    [Fact]
    public void Split_PrefersParagraphBoundaries()
    {
        string paragraphs = string.Join("\n\n", Enumerable.Range(0, 12)
            .Select(p => string.Join(" ", Enumerable.Range(0, 40).Select(w => $"p{p}w{w}"))));

        string normalized = TextChunker.Normalize(paragraphs);
        IReadOnlyList<string> chunks = TextChunker.Split(paragraphs, MaxChunkSize, OverlapSize);

        chunks.Should().OnlyContain(chunk => chunk.Length <= MaxChunkSize);
        chunks.Count(chunk => normalized.Contains(chunk + "\n\n", StringComparison.Ordinal))
            .Should().BeGreaterThan(0, "at least some chunks should end where a paragraph does");
    }

    [Fact]
    public void Split_WhenOneTokenExceedsAChunk_TerminatesAndHardCuts()
    {
        // The termination hazard: no whitespace anywhere to break on.
        IReadOnlyList<string> chunks = TextChunker.Split(new string('x', 5_000), MaxChunkSize, OverlapSize);

        chunks.Should().OnlyContain(chunk => chunk.Length <= MaxChunkSize);
        chunks.Sum(chunk => chunk.Length).Should().BeGreaterThanOrEqualTo(5_000, "no text may be dropped");
    }

    [Fact]
    public void Split_WhenTextMixesShortWordsAndAGiantToken_Terminates()
    {
        string text = "short intro. " + new string('y', 3_000) + " tail words here";

        IReadOnlyList<string> chunks = TextChunker.Split(text, MaxChunkSize, OverlapSize);

        chunks.Should().NotBeEmpty();
        chunks.Should().OnlyContain(chunk => chunk.Length <= MaxChunkSize);
    }

    [Fact]
    public void Split_IsDeterministic()
    {
        string text = Lorem(900);

        TextChunker.Split(text, MaxChunkSize, OverlapSize)
            .Should().Equal(TextChunker.Split(text, MaxChunkSize, OverlapSize));
    }

    [Theory]
    [InlineData("a\r\nb")]
    [InlineData("a\rb")]
    public void Normalize_RemovesCarriageReturns(string text)
    {
        TextChunker.Normalize(text).Should().NotContain("\r");
    }

    [Fact]
    public void Normalize_CollapsesRunsOfBlankLines()
    {
        TextChunker.Normalize("a\n\n\n\n\nb").Should().Be("a\n\nb");
    }

    [Fact]
    public void Normalize_StripsTrailingLineWhitespaceWithoutCreatingAParagraphBreak()
    {
        // The regression: a lookahead-matched replacement once inserted a second
        // newline here, turning every line with a trailing space into a paragraph
        // break. PDF-extracted text has trailing spaces on nearly every line.
        TextChunker.Normalize("a   \nb").Should().Be("a\nb");
    }

    [Fact]
    public void Split_HandlesCrlfParagraphs()
    {
        TextChunker.Split("para one\r\n\r\npara two", MaxChunkSize, OverlapSize).Should().ContainSingle();
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(800, -1)]
    [InlineData(800, 800)]
    [InlineData(800, 900)]
    public void Split_WithInvalidSizing_Throws(int maxChunkSize, int overlapSize)
    {
        Action act = () => TextChunker.Split("abc", maxChunkSize, overlapSize);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Split_OverManyRandomDocuments_TerminatesAndHoldsItsInvariants()
    {
        // The case that found the leading-whitespace bug. Fixed seed, so a failure
        // is reproducible rather than a story about a flaky build.
        var random = new Random(20260801);

        for (int iteration = 0; iteration < 400; iteration++)
        {
            string text = RandomDocument(random);
            int maxChunkSize = random.Next(20, 1200);
            int overlapSize = random.Next(0, maxChunkSize);

            IReadOnlyList<string> chunks = TextChunker.Split(text, maxChunkSize, overlapSize);

            chunks.Should().OnlyContain(
                chunk => chunk.Length <= maxChunkSize,
                $"iteration {iteration} used maxChunkSize {maxChunkSize}");

            HashSet<string> covered = [.. chunks.SelectMany(Words)];

            // Words longer than a chunk must be fragmented; every other word must
            // survive intact.
            IEnumerable<string> lost = Words(TextChunker.Normalize(text))
                .Where(word => word.Length <= maxChunkSize && !covered.Contains(word));

            lost.Should().BeEmpty($"iteration {iteration} lost a word that should have fitted");
        }
    }

    private static string RandomDocument(Random random)
    {
        var builder = new StringBuilder();
        int target = random.Next(0, 6_000);

        while (builder.Length < target)
        {
            int roll = random.Next(100);

            if (roll < 8)
            {
                builder.Append("\n\n");
            }
            else if (roll < 20)
            {
                builder.Append('\n');
            }
            else if (roll < 30)
            {
                builder.Append(' ');
            }
            else
            {
                builder.Append(new string((char)('a' + random.Next(26)), random.Next(1, 60))).Append(' ');
            }
        }

        return builder.ToString();
    }
}
