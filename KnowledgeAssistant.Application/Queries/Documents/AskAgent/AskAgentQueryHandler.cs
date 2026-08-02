using FluentValidation;
using FluentValidation.Results;
using KnowledgeAssistant.Application.Abstractions;
using KnowledgeAssistant.Application.Common;
using KnowledgeAssistant.Application.Interfaces;
using KnowledgeAssistant.Domain.Common;
using Microsoft.Extensions.Logging;

namespace KnowledgeAssistant.Application.Queries.Documents.AskAgent;

/// <summary>
/// Answers a question by delegating to the agent.
/// </summary>
/// <remarks>
/// <para>
/// <b>This handler is shorter than the retrieval one, and that is the point.</b>
/// There is no pipeline to sequence here: embedding, searching, prompting, and
/// deciding how many times to do each are the agent's business, behind one port.
/// What is left is validation, one call, and turning what came back into a
/// response — which is exactly as much as a use case should own when the
/// orchestration genuinely belongs elsewhere.
/// </para>
/// <para>
/// <b>What did not move behind the port.</b> The acceptance rules, the citation
/// contract, and the shape of the response all stay here, because all three are
/// promises this system makes to its callers rather than capabilities it buys from
/// a service. Swapping the agent runtime changes the adapter and nothing in this
/// file.
/// </para>
/// <para>
/// <b>Service errors pass through unaltered</b>, as in every other slice: an agent
/// failure surfaces as <c>Agent.*</c>, and the error code is what tells an
/// operator which stage broke. The handler adds no error of its own, because
/// nothing here can fail that the validator did not already reject.
/// </para>
/// <para>
/// <b>Nothing here writes.</b> The query touches no blob and no index, so a
/// failure leaves no state to reconcile.
/// </para>
/// </remarks>
internal sealed partial class AskAgentQueryHandler
    : IQueryHandler<AskAgentQuery, AskAgentResponse>
{
    private readonly IValidator<AskAgentQuery> _validator;
    private readonly IAgentService _agentService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AskAgentQueryHandler> _logger;

    /// <summary>Initialises the handler.</summary>
    public AskAgentQueryHandler(
        IValidator<AskAgentQuery> validator,
        IAgentService agentService,
        TimeProvider timeProvider,
        ILogger<AskAgentQueryHandler> logger)
    {
        _validator = validator;
        _agentService = agentService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Runs the agent and shapes its answer.</summary>
    public async Task<Result<AskAgentResponse>> Handle(
        AskAgentQuery request,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator
            .ValidateAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (!validationResult.IsValid)
        {
            return Result.Failure<AskAgentResponse>(validationResult.ToValidationError());
        }

        long startedAt = _timeProvider.GetTimestamp();

        // The question itself is never logged, for the same reason it is not
        // logged in the retrieval slice: it is user input, plausibly sensitive,
        // and its length and outcome are what an operator needs.
        LogQuestionReceived(request.Question.Length, request.MaxSources);

        Result<AgentAnswer> agentResult = await _agentService
            .AskAsync(new AgentQuestion(request.Question, request.MaxSources), cancellationToken)
            .ConfigureAwait(false);

        double elapsedMs = _timeProvider.GetElapsedTime(startedAt).TotalMilliseconds;

        if (agentResult.IsFailure)
        {
            LogAgentFailed(agentResult.Error.Code, elapsedMs);
            return Result.Failure<AskAgentResponse>(agentResult.Error);
        }

        AgentAnswer answer = agentResult.Value;

        // Logged at Warning because it is the failure mode that does not look like
        // one: the caller receives a fluent, confident answer that the corpus had
        // no part in. It is reported rather than rejected — a question that is not
        // about the documents deserves a straight refusal, not a 500 — so a rising
        // rate of this line is the signal, not any single occurrence.
        if (answer.SearchCount == 0)
        {
            LogAnsweredWithoutSearching(elapsedMs);
        }

        LogQuestionAnswered(
            answer.SearchCount,
            answer.ConsultedSources.Count,
            answer.Usage?.TotalTokens ?? 0,
            elapsedMs);

        return new AskAgentResponse(
            Answer: answer.Content,
            Citations: BuildCitations(answer.ConsultedSources),
            SearchCount: answer.SearchCount,
            TokenUsage: answer.Usage,
            AnsweredAtUtc: _timeProvider.GetUtcNow());
    }

    /// <summary>
    /// Numbers the consulted passages exactly as the agent was shown them.
    /// </summary>
    /// <remarks>
    /// The port guarantees <see cref="AgentAnswer.ConsultedSources"/> is already
    /// deduplicated and in first-seen order, so a one-based index over it is the
    /// same number the agent was told to cite. Renumbering here — sorting by
    /// score, say — would silently break every <c>[n]</c> in the answer text,
    /// which is the kind of defect that reads as a model failure rather than a
    /// code one.
    /// </remarks>
    private static AgentCitation[] BuildCitations(IReadOnlyList<ChunkSearchResult> sources)
    {
        var citations = new AgentCitation[sources.Count];

        for (int index = 0; index < sources.Count; index++)
        {
            ChunkSearchResult source = sources[index];

            citations[index] = new AgentCitation(
                ReferenceNumber: index + 1,
                ChunkId: source.ChunkId,
                DocumentId: source.DocumentId,
                ChunkOrder: source.ChunkOrder,
                Text: source.Text,
                BlobUri: source.BlobUri,
                Score: source.Score);
        }

        return citations;
    }

    // Structured logging. The question and the answer are never logged — they are
    // user content — so each line carries shape, cost, and timing instead.

    [LoggerMessage(
        EventId = 7100,
        Level = LogLevel.Information,
        Message = "Agent question received ({QuestionLength} characters, up to {MaxSources} sources per search).")]
    private partial void LogQuestionReceived(int questionLength, int maxSources);

    [LoggerMessage(
        EventId = 7101,
        Level = LogLevel.Information,
        Message = "Agent answered after {SearchCount} searches over {SourceCount} sources, " +
                  "{TotalTokens} tokens, in {ElapsedMs:F0}ms.")]
    private partial void LogQuestionAnswered(
        int searchCount, int sourceCount, int totalTokens, double elapsedMs);

    [LoggerMessage(
        EventId = 7102,
        Level = LogLevel.Warning,
        Message = "The agent answered without searching the corpus in {ElapsedMs:F0}ms. " +
                  "The answer is not grounded in any indexed document.")]
    private partial void LogAnsweredWithoutSearching(double elapsedMs);

    [LoggerMessage(
        EventId = 7103,
        Level = LogLevel.Warning,
        Message = "The agent failed with {ErrorCode} after {ElapsedMs:F0}ms.")]
    private partial void LogAgentFailed(string errorCode, double elapsedMs);
}
