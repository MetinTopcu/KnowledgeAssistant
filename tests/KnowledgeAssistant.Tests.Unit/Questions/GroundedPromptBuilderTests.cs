using KnowledgeAssistant.Application.Interfaces;
using KnowledgeAssistant.Application.Queries.Documents.Ask;

namespace KnowledgeAssistant.Tests.Unit.Questions;

/// <summary>
/// The grounding prompt: instruction, numbered sources, and the context budget.
/// </summary>
/// <remarks>
/// <para>
/// This is the type that decides what the model is allowed to see, so it is
/// where grounding either holds or quietly stops holding. A prompt that loses
/// its numbering breaks every citation downstream; one that loses its
/// instruction turns a retrieval system into an ordinary chatbot that happens to
/// have read some documents.
/// </para>
/// <para>
/// The assertions look at the built messages rather than at a golden string.
/// Pinning the exact prompt text would fail on every wording change while
/// proving nothing about the properties that matter.
/// </para>
/// </remarks>
public sealed class GroundedPromptBuilderTests
{
    private static ChunkSearchResult Chunk(int index, int textLength = 50) => new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        index,
        new string((char)('a' + (index % 26)), textLength),
        new Uri($"https://acct.blob.core.windows.net/documents/doc{index}.pdf"),
        0.9 - (index * 0.1));

    private static IReadOnlyList<ChunkSearchResult> Chunks(int count, int textLength = 50) =>
        [.. Enumerable.Range(0, count).Select(index => Chunk(index, textLength))];

    [Fact]
    public void Build_ProducesASystemInstructionFollowedByTheUserTurn()
    {
        IReadOnlyList<ChatMessage> messages = GroundedPromptBuilder.Build("What is the policy?", Chunks(3));

        messages.Should().HaveCount(2);
        messages[0].Role.Should().Be(ChatRole.System);
        messages[1].Role.Should().Be(ChatRole.User);
        messages[0].Content.Should().Be(GroundedPromptBuilder.SystemPrompt);
    }

    [Fact]
    public void SystemPrompt_ConfinesTheModelToTheSuppliedSources()
    {
        // Three separate obligations, each one load-bearing: answer only from the
        // sources, cite them, and decline rather than invent when they fall short.
        GroundedPromptBuilder.SystemPrompt.Should().Contain("ONLY")
            .And.Contain("Do not use prior knowledge")
            .And.Contain("[1]")
            .And.Contain("do not contain enough information");
    }

    [Fact]
    public void Build_NumbersTheSourcesFromOneInRankOrder()
    {
        IReadOnlyList<ChunkSearchResult> chunks = Chunks(3);

        string user = GroundedPromptBuilder.Build("What is the policy?", chunks)[1].Content;

        user.Should().Contain("[1]").And.Contain("[2]").And.Contain("[3]");
        user.IndexOf(chunks[0].Text, StringComparison.Ordinal)
            .Should().BeLessThan(user.IndexOf(chunks[1].Text, StringComparison.Ordinal),
                "sources must appear in relevance order so [1] is the strongest match");
    }

    [Fact]
    public void Build_IncludesEverySourcesTextAndPlacesTheQuestionLast()
    {
        IReadOnlyList<ChunkSearchResult> chunks = Chunks(3);

        string user = GroundedPromptBuilder.Build("What is the policy?", chunks)[1].Content;

        chunks.Should().OnlyContain(chunk => user.Contains(chunk.Text, StringComparison.Ordinal));
        user.IndexOf("[1]", StringComparison.Ordinal)
            .Should().BeLessThan(user.IndexOf("What is the policy?", StringComparison.Ordinal),
                "the question reads as an instruction about the evidence above it");
    }

    [Fact]
    public void CountChunksThatFit_AcceptsEverythingWhenTheChunksAreSmall()
    {
        GroundedPromptBuilder.CountChunksThatFit(Chunks(2)).Should().Be(2);
    }

    [Fact]
    public void CountChunksThatFit_StopsAtTheContextBudget()
    {
        // Four chunks of 10k against a 24k budget: two fit, and the budget must be
        // enforced by dropping sources rather than by truncating one mid-sentence.
        int fitting = GroundedPromptBuilder.CountChunksThatFit(Chunks(4, textLength: 10_000));

        fitting.Should().Be(2, $"the budget is {GroundedPromptBuilder.MaxContextCharacters} characters");
    }

    [Fact]
    public void Build_DropsTheLeastRelevantSourcesWhenTheBudgetIsExceeded()
    {
        IReadOnlyList<ChunkSearchResult> chunks = Chunks(4, textLength: 10_000);

        string user = GroundedPromptBuilder.Build("Q?", chunks)[1].Content;

        user.Should().Contain(chunks[0].Text, "the highest-ranked source must survive truncation");
        user.Should().Contain(chunks[1].Text);
        user.Should().NotContain(chunks[2].Text, "the weakest sources are the ones to drop");
        user.Should().Contain("Q?", "the question must survive truncation");
    }

    [Fact]
    public void Build_AndCountChunksThatFit_Agree()
    {
        // They must, or the citations built from the count would credit a source
        // the model was never shown.
        IReadOnlyList<ChunkSearchResult> chunks = Chunks(4, textLength: 10_000);

        int fitting = GroundedPromptBuilder.CountChunksThatFit(chunks);
        string user = GroundedPromptBuilder.Build("Q?", chunks)[1].Content;

        chunks.Take(fitting).Should().OnlyContain(chunk => user.Contains(chunk.Text, StringComparison.Ordinal));
        chunks.Skip(fitting).Should().OnlyContain(chunk => !user.Contains(chunk.Text, StringComparison.Ordinal));
    }
}
