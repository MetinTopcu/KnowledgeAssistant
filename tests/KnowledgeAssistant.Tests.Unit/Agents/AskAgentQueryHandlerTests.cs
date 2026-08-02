using KnowledgeAssistant.Application.Interfaces;
using KnowledgeAssistant.Application.Queries.Documents.AskAgent;
using KnowledgeAssistant.Domain.Common;
using KnowledgeAssistant.Tests.Unit.Fakes;

namespace KnowledgeAssistant.Tests.Unit.Agents;

/// <summary>
/// The agent slice: validate, delegate, shape the answer.
/// </summary>
/// <remarks>
/// Short, because the handler is. The tests worth reading are the citation ones
/// and the zero-search one: the first guards a promise about provenance, and the
/// second guards the only signal that separates a grounded answer from a fluent
/// invention.
/// </remarks>
public sealed class AskAgentQueryHandlerTests
{
    [Fact]
    public async Task Handle_PassesTheQuestionAndSourceBoundToTheAgent()
    {
        var rig = new PipelineRig();

        await rig.CreateAgentHandler()
            .Handle(new AskAgentQuery("What is the refund policy?", MaxSources: 7), CancellationToken.None);

        rig.Agent.ReceivedQuestion.Should().Be(new AgentQuestion("What is the refund policy?", 7),
            "the caller's question and retrieval depth are not the handler's to alter");
    }

    [Fact]
    public async Task Handle_ReturnsTheAgentsAnswerWithItsSearchCountAndTokenUsage()
    {
        var rig = new PipelineRig();
        rig.Agent.SearchCount = 3;

        Result<AskAgentResponse> result = await rig.CreateAgentHandler()
            .Handle(new AskAgentQuery("Q?", MaxSources: 5), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Answer.Should().Be(rig.Agent.Answer);
        result.Value.SearchCount.Should().Be(3);
        result.Value.TokenUsage.Should().Be(rig.Agent.Usage, "cost reporting is part of the response contract");
    }

    [Fact]
    public async Task Handle_NumbersCitationsInTheOrderTheAgentConsultedThem()
    {
        // The citation contract. The port promises its sources are deduplicated and
        // in first-seen order, so a one-based index over them is the same number the
        // agent was told to cite. Reordering here — by score, say — would silently
        // break every [n] in the answer text, and the defect would read as a model
        // failure rather than a code one.
        var rig = new PipelineRig();

        Result<AskAgentResponse> result = await rig.CreateAgentHandler()
            .Handle(new AskAgentQuery("Q?", MaxSources: 5), CancellationToken.None);

        IReadOnlyList<AgentCitation> citations = result.Value.Citations;

        citations.Should().HaveCount(rig.Agent.Sources.Count);
        citations.Select(citation => citation.ReferenceNumber).Should().Equal(1, 2);
        citations.Select(citation => citation.ChunkId)
            .Should().Equal(rig.Agent.Sources.Select(source => source.ChunkId));
        citations.Select(citation => citation.Text)
            .Should().Equal(rig.Agent.Sources.Select(source => source.Text));
    }

    [Fact]
    public async Task Handle_WhenTheAgentNeverSearched_StillSucceedsAndSaysSo()
    {
        // An agent is allowed to decline a question that is not about the corpus,
        // and failing the request would turn a correct refusal into a 500. The
        // count is what makes the ungrounded case visible instead.
        var rig = new PipelineRig();
        rig.Agent.SearchCount = 0;
        rig.Agent.Sources.Clear();

        Result<AskAgentResponse> result = await rig.CreateAgentHandler()
            .Handle(new AskAgentQuery("Who won the 1998 world cup?", MaxSources: 5), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.SearchCount.Should().Be(0, "an unsearched answer must be distinguishable from a grounded one");
        result.Value.Citations.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenTheAgentFails_PassesTheErrorCodeThrough()
    {
        // The error code is what tells an operator which stage broke, so the handler
        // must not replace a retrieval failure with a generic agent one.
        var rig = new PipelineRig();
        rig.Agent.Error = Error.Failure("Search.QueryFailed", "nope");

        Result<AskAgentResponse> result = await rig.CreateAgentHandler()
            .Handle(new AskAgentQuery("Q?", MaxSources: 5), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Search.QueryFailed");
    }

    [Theory]
    [InlineData("", 5)]
    [InlineData("   ", 5)]
    [InlineData("Q?", 0)]
    [InlineData("Q?", 21)]
    public async Task Handle_WithAnInvalidRequest_FailsBeforeCallingTheAgent(string question, int maxSources)
    {
        // Validation is a cost control here more than a correctness one: every
        // accepted question spends at least one model call, and an agent may spend
        // several. This is the cheapest place to say no.
        var rig = new PipelineRig();

        Result<AskAgentResponse> result = await rig.CreateAgentHandler()
            .Handle(new AskAgentQuery(question, maxSources), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        rig.Agent.CallCount.Should().Be(0, "nothing should be spent on a request that was never valid");
    }

    [Fact]
    public async Task Handle_PassesTheCallersCancellationTokenToTheAgent()
    {
        var rig = new PipelineRig();
        using var cancellation = new CancellationTokenSource();

        await rig.CreateAgentHandler()
            .Handle(new AskAgentQuery("Q?", MaxSources: 5), cancellation.Token);

        rig.Trace.Stages.Should().Equal("Agent");
        rig.Trace.Tokens.Should().AllSatisfy(token => token.Should().Be(cancellation.Token),
            "a caller who disconnects should not keep paying for an agent run");
    }
}
