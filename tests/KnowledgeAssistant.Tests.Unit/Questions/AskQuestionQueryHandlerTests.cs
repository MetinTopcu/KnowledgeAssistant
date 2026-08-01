using KnowledgeAssistant.Application.Queries.Documents.Ask;
using KnowledgeAssistant.Domain.Common;
using KnowledgeAssistant.Tests.Unit.Fakes;

namespace KnowledgeAssistant.Tests.Unit.Questions;

/// <summary>
/// The retrieval-augmented answer pipeline: embed, search, answer, cite.
/// </summary>
/// <remarks>
/// <para>
/// The citation assertions are the ones worth reading twice. A citation that
/// points at a source the model was never given is worse than no citation at
/// all: it is a fabricated provenance that looks exactly like a real one, and
/// the only place it can be caught is here.
/// </para>
/// <para>
/// Unlike ingestion, this pipeline writes nothing, so a failure at any stage
/// leaves no state to reconcile. The failure tests therefore assert where the
/// pipeline stopped rather than what it left behind.
/// </para>
/// </remarks>
public sealed class AskQuestionQueryHandlerTests
{
    private static readonly string[] FullPipeline = ["EmbedText", "Search", "Chat"];

    [Fact]
    public async Task Handle_RunsEmbedThenSearchThenChat()
    {
        var rig = new PipelineRig();

        Result<AskQuestionResponse> result = await rig.CreateAskHandler()
            .Handle(new AskQuestionQuery("What is the refund policy?", TopK: 3), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        rig.Trace.Stages.Should().Equal(FullPipeline, "the pipeline sequence is the contract");
    }

    [Fact]
    public async Task Handle_EmbedsTheQuestionAndSearchesWithThatVector()
    {
        var rig = new PipelineRig();

        await rig.CreateAskHandler()
            .Handle(new AskQuestionQuery("What is the refund policy?", TopK: 3), CancellationToken.None);

        rig.Embedding.ReceivedText.Should().Be("What is the refund policy?");
        rig.Search.ReceivedVector.Span.SequenceEqual(rig.Embedding.QueryVector.Span).Should().BeTrue(
            "the vector searched must be the one produced for this question");
        rig.Search.ReceivedTopK.Should().Be(3, "the caller's retrieval depth is not the handler's to override");
    }

    [Fact]
    public async Task Handle_ReturnsTheModelsAnswerWithItsTokenUsage()
    {
        var rig = new PipelineRig();

        Result<AskQuestionResponse> result = await rig.CreateAskHandler()
            .Handle(new AskQuestionQuery("Q?", TopK: 3), CancellationToken.None);

        result.Value.Answer.Should().Be(rig.Chat.Answer);
        result.Value.RetrievedChunkCount.Should().Be(3);
        result.Value.TokenUsage.Should().Be(rig.Chat.Usage, "cost reporting is part of the response contract");
    }

    [Fact]
    public async Task Handle_EmitsOneCitationPerGroundingChunkNumberedFromOne()
    {
        var rig = new PipelineRig();
        rig.Search.ResultCount = 4;

        Result<AskQuestionResponse> result = await rig.CreateAskHandler()
            .Handle(new AskQuestionQuery("Q?", TopK: 4), CancellationToken.None);

        IReadOnlyList<AnswerCitation> citations = result.Value.Citations;

        citations.Should().HaveCount(4);
        citations.Select(citation => citation.ReferenceNumber).Should().Equal(1, 2, 3, 4);
        citations.Select(citation => citation.ChunkId).Should().Equal(rig.Search.Produced.Select(chunk => chunk.ChunkId));
        citations.Select(citation => citation.Text).Should().Equal(rig.Search.Produced.Select(chunk => chunk.Text));
        citations.Select(citation => citation.Score).Should().Equal(rig.Search.Produced.Select(chunk => chunk.Score));
        citations.Select(citation => citation.BlobUri).Should().Equal(rig.Search.Produced.Select(chunk => chunk.BlobUri));
    }

    [Fact]
    public async Task Handle_NumbersCitationsToMatchTheSourceLabelsInThePrompt()
    {
        // Citation [1] must be the source the prompt labelled [1]. If the two
        // numberings drift apart, every answer cites confidently and wrongly.
        var rig = new PipelineRig();
        rig.Search.ResultCount = 4;

        Result<AskQuestionResponse> result = await rig.CreateAskHandler()
            .Handle(new AskQuestionQuery("Q?", TopK: 4), CancellationToken.None);

        string prompt = rig.Chat.ReceivedMessages![1].Content;
        int positionOfFirstCitation = prompt.IndexOf(result.Value.Citations[0].Text, StringComparison.Ordinal);

        positionOfFirstCitation.Should().BeGreaterThan(prompt.IndexOf("[1]", StringComparison.Ordinal));
        positionOfFirstCitation.Should().BeLessThan(prompt.IndexOf("[2]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Handle_LimitsCitationsToTheSourcesThatFitInThePrompt()
    {
        // Retrieval found four; the context budget admitted two. Citing all four
        // would credit the answer to text the model never saw.
        var rig = new PipelineRig();
        rig.Search.ResultCount = 4;
        rig.Search.ResultTextLength = 10_000;

        Result<AskQuestionResponse> result = await rig.CreateAskHandler()
            .Handle(new AskQuestionQuery("Q?", TopK: 4), CancellationToken.None);

        result.Value.Citations.Should().HaveCount(2, "only what fitted may be cited");
        result.Value.RetrievedChunkCount.Should().Be(4, "the retrieved count still reports everything found");
    }

    [Fact]
    public async Task Handle_WhenRetrievalIsEmpty_AnswersWithoutCallingTheModel()
    {
        // A success, not a failure: "the corpus does not cover this" is a valid
        // answer to a valid question. Calling the model with no evidence would
        // spend money to obtain an ungrounded guess.
        var rig = new PipelineRig();
        rig.Search.ResultCount = 0;

        Result<AskQuestionResponse> result = await rig.CreateAskHandler()
            .Handle(new AskQuestionQuery("Anything about unicorns?", TopK: 5), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        rig.Chat.CallCount.Should().Be(0);
        rig.Trace.Stages.Should().Equal("EmbedText", "Search");
        result.Value.Answer.Should().Be(GroundedPromptBuilder.NoEvidenceAnswer);
        result.Value.Citations.Should().BeEmpty();
        result.Value.RetrievedChunkCount.Should().Be(0);
        result.Value.TokenUsage.Should().BeNull("no model call means no tokens to report");
    }

    [Theory]
    [InlineData("EmbedText", "Embedding.RateLimited")]
    [InlineData("Search", "Search.ChunkIndexUnavailable")]
    [InlineData("Chat", "Chat.RateLimited")]
    public async Task Handle_WhenAStageFails_StopsThereAndPassesTheErrorThroughUnwrapped(
        string failingStage,
        string expectedCode)
    {
        var rig = new PipelineRig();
        FailAt(rig, failingStage, expectedCode);

        Result<AskQuestionResponse> result = await rig.CreateAskHandler()
            .Handle(new AskQuestionQuery("Q?", TopK: 5), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(expectedCode);
        rig.Trace.Stages.Should().Equal(
            FullPipeline.TakeWhile(stage => stage != failingStage).Append(failingStage),
            $"the pipeline must stop at {failingStage}. Ran: {rig.Trace.Sequence}");
    }

    [Fact]
    public async Task Handle_PassesTheCallersTokenToEveryStage()
    {
        var rig = new PipelineRig();
        using var cts = new CancellationTokenSource();

        await rig.CreateAskHandler().Handle(new AskQuestionQuery("Q?", TopK: 5), cts.Token);

        rig.Trace.Tokens.Should().HaveCount(3);
        rig.Trace.Tokens.Should().OnlyContain(token => token == cts.Token);
    }

    [Theory]
    [InlineData("", 5, "an empty question")]
    [InlineData("   ", 5, "a whitespace-only question")]
    [InlineData("Q?", 0, "a topK below the floor")]
    [InlineData("Q?", 21, "a topK above the ceiling")]
    public async Task Handle_WhenValidationFails_CallsNothing(string question, int topK, string because)
    {
        var rig = new PipelineRig();

        Result<AskQuestionResponse> result = await rig.CreateAskHandler()
            .Handle(new AskQuestionQuery(question, topK), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        rig.Trace.Stages.Should().BeEmpty($"{because} must be rejected before an embedding is paid for");
    }

    [Fact]
    public async Task Handle_WhenTheQuestionExceedsTheLengthLimit_CallsNothing()
    {
        var rig = new PipelineRig();

        Result<AskQuestionResponse> result = await rig.CreateAskHandler()
            .Handle(new AskQuestionQuery(new string('x', AskQuestionQueryValidator.MaxQuestionLength + 1), 5),
                CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        rig.Trace.Stages.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_AcceptsTopKAtTheCeiling()
    {
        // The boundary itself, because an off-by-one in the validator would make
        // the documented maximum unusable and no other test would notice.
        var rig = new PipelineRig();

        Result<AskQuestionResponse> result = await rig.CreateAskHandler()
            .Handle(new AskQuestionQuery("Q?", AskQuestionQueryValidator.MaxTopK), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    private static void FailAt(PipelineRig rig, string stage, string code)
    {
        Error error = Error.Failure(code, "scripted failure");

        switch (stage)
        {
            case "EmbedText":
                rig.Embedding.TextError = error;
                break;
            case "Search":
                rig.Search.SearchError = error;
                break;
            case "Chat":
                rig.Chat.Error = error;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(stage), stage, "unknown pipeline stage");
        }
    }
}
