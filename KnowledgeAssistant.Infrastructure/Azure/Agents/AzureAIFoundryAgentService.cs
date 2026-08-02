using System.ClientModel;
using Azure.AI.Extensions.OpenAI;
using Azure.Identity;
using KnowledgeAssistant.Application.Interfaces;
using KnowledgeAssistant.Domain.Common;
using Microsoft.Extensions.Logging;
using OpenAI.Responses;
using Polly;

namespace KnowledgeAssistant.Infrastructure.Azure.Agents;

/// <summary>
/// Builds the responses client bound to a resolved agent version.
/// </summary>
/// <remarks>
/// A factory rather than a registered client because the client's identity
/// includes the agent version, and that version is only known after the first call
/// resolves it. Every other Azure client in this solution is built in the
/// composition root; this delegate keeps that true — the endpoint, the credential,
/// and the pipeline options are still chosen there, and only the one value the
/// adapter discovers is supplied here.
/// </remarks>
internal delegate ProjectResponsesClient FoundryResponsesClientFactory(AgentReference agent);

/// <summary>
/// Answers questions with an Azure AI Foundry agent that searches this system's
/// own corpus.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this adapter actually owns.</b> Three things, and nothing else: the
/// conversation loop between the agent and its tool, the translation between the
/// Responses protocol and this solution's port, and the failure taxonomy. It holds
/// no prompt — that is <c>AgentInstructions</c>, in Application, because it is a
/// product rule — and it performs no retrieval of its own, delegating every search
/// to <see cref="KnowledgeSearchTool"/>, which in turn delegates to the same
/// embedding and search ports the retrieval slice uses.
/// </para>
/// <para>
/// <b>The loop is here rather than in the service.</b> Foundry runs the agent's
/// reasoning but not its tools: a run pauses with a function call and waits for
/// this process to answer it. That is exactly what makes reusing the existing RAG
/// pipeline possible — the search happens in-process, through the ports, with this
/// solution's retry policy and error codes — and it is why the number of round
/// trips is bounded here rather than trusted to the model.
/// </para>
/// <para>
/// <b>Registered as a singleton.</b> The clients are thread-safe and hold pooled
/// connections and a cached token, the resilience pipeline is immutable and
/// reusable, and the resolved agent version is cached for the process. Per-request
/// construction would re-run the credential chain, discard the retry state that
/// makes backoff meaningful, and re-resolve the agent on every question.
/// </para>
/// <para>
/// <b>The retry policy is the embedding and chat adapters', shared rather than
/// copied.</b> The agent runs on the same AI Foundry resource, fails the same ways
/// — 429 above all — and a third policy would drift from the first two the moment
/// any of them was tuned.
/// </para>
/// <para>
/// <b>Per-search failures fail the whole request.</b> An embedding or search
/// failure is returned to the caller with its own error code intact —
/// <c>Embedding.*</c> or <c>Search.*</c> — rather than being reported to the agent.
/// Telling a model its search is broken invites it to answer from prior knowledge
/// at exactly the moment this system knows it cannot ground an answer.
/// </para>
/// </remarks>
internal sealed partial class AzureAIFoundryAgentService : IAgentService, IDisposable
{
    private readonly FoundryResponsesClientFactory _clientFactory;
    private readonly FoundryAgentProvisioner _provisioner;
    private readonly KnowledgeSearchTool _searchTool;
    private readonly FoundryAgentOptions _options;
    private readonly ResiliencePipeline _resiliencePipeline;
    private readonly ILogger<AzureAIFoundryAgentService> _logger;

    // Built once, on the first question, because it is keyed by the agent version
    // the provisioner resolves. Guarded by the same reasoning as the provisioner's
    // own lock: several questions can arrive together on a cold process.
    private readonly SemaphoreSlim _clientLock = new(1, 1);
    private ProjectResponsesClient? _client;

    /// <summary>Initialises the adapter.</summary>
    public AzureAIFoundryAgentService(
        FoundryResponsesClientFactory clientFactory,
        FoundryAgentProvisioner provisioner,
        KnowledgeSearchTool searchTool,
        FoundryAgentOptions options,
        ResiliencePipeline resiliencePipeline,
        ILogger<AzureAIFoundryAgentService> logger)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(provisioner);
        ArgumentNullException.ThrowIfNull(searchTool);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(resiliencePipeline);

        _clientFactory = clientFactory;
        _provisioner = provisioner;
        _searchTool = searchTool;
        _options = options;
        _resiliencePipeline = resiliencePipeline;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<AgentAnswer>> AskAsync(
        AgentQuestion question,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);

        if (string.IsNullOrWhiteSpace(question.Question))
        {
            throw new ArgumentException("A question is required.", nameof(question));
        }

        Result<ProjectResponsesClient> clientResult =
            await GetClientAsync(cancellationToken).ConfigureAwait(false);

        if (clientResult.IsFailure)
        {
            return Result.Failure<AgentAnswer>(clientResult.Error);
        }

        var run = new AgentRun(question.MaxSources);

        IReadOnlyList<ResponseItem> input = [ResponseItem.CreateUserMessageItem(question.Question)];
        string? previousResponseId = null;
        bool searchesPermitted = true;
        string answer = string.Empty;

        while (true)
        {
            Result<ResponseResult> responseResult = await CreateResponseAsync(
                clientResult.Value, input, previousResponseId, cancellationToken).ConfigureAwait(false);

            if (responseResult.IsFailure)
            {
                return Result.Failure<AgentAnswer>(responseResult.Error);
            }

            ResponseResult response = responseResult.Value;

            run.AddUsage(response.Usage);
            previousResponseId = response.Id;
            answer = response.GetOutputText() ?? string.Empty;

            List<FunctionCallResponseItem> calls = [.. response.OutputItems.OfType<FunctionCallResponseItem>()];

            if (calls.Count == 0)
            {
                // No pending calls: the agent has finished and this response holds
                // the answer.
                LogRunCompleted(response.Status, run.SearchCount, run.TotalTokens);
                break;
            }

            if (!searchesPermitted)
            {
                // The agent was told searching was over and called anyway. Answering
                // the calls again would loop, so the run stops here with whatever
                // text it has produced — which is usually none, and is then reported
                // as no answer below.
                LogIgnoredCallsAfterBudget(calls.Count);
                break;
            }

            if (run.SearchRounds >= _options.MaxToolIterations)
            {
                // Every pending call still has to be answered — the protocol will
                // not accept the next turn otherwise — so the budget is spent by
                // replying that searching is over rather than by abandoning the run.
                LogSearchBudgetExhausted(_options.MaxToolIterations, run.SearchCount);

                input = [.. calls.Select(call =>
                    ResponseItem.CreateFunctionCallOutputItem(
                        call.CallId,
                        KnowledgeSearchTool.RenderBudgetExhausted()))];

                searchesPermitted = false;
                continue;
            }

            Result<IReadOnlyList<ResponseItem>> outputs =
                await ExecuteCallsAsync(calls, run, cancellationToken).ConfigureAwait(false);

            if (outputs.IsFailure)
            {
                return Result.Failure<AgentAnswer>(outputs.Error);
            }

            run.SearchRounds++;
            input = outputs.Value;
        }

        if (string.IsNullOrWhiteSpace(answer))
        {
            LogNoAnswer(run.SearchCount);
            return Result.Failure<AgentAnswer>(AgentErrors.NoAnswerGenerated);
        }

        return new AgentAnswer(
            Content: answer,
            ConsultedSources: run.Sources,
            SearchCount: run.SearchCount,
            Usage: run.ToTokenUsage());
    }

    /// <summary>
    /// Answers every pending tool call in one round.
    /// </summary>
    /// <remarks>
    /// Sequential rather than concurrent. An agent that issues parallel searches is
    /// issuing them against one rate-limited embedding deployment, so running them
    /// together buys a little latency in exchange for making a 429 — and the
    /// backoff that follows — considerably more likely.
    /// </remarks>
    private async Task<Result<IReadOnlyList<ResponseItem>>> ExecuteCallsAsync(
        List<FunctionCallResponseItem> calls,
        AgentRun run,
        CancellationToken cancellationToken)
    {
        var outputs = new List<ResponseItem>(calls.Count);

        foreach (FunctionCallResponseItem call in calls)
        {
            if (!string.Equals(call.FunctionName, KnowledgeSearchTool.FunctionName, StringComparison.Ordinal))
            {
                LogUnknownFunction(call.FunctionName);

                outputs.Add(ResponseItem.CreateFunctionCallOutputItem(
                    call.CallId, KnowledgeSearchTool.RenderUnknownFunction()));

                continue;
            }

            string? query = KnowledgeSearchTool.TryReadQuery(call.FunctionArguments);

            if (query is null)
            {
                LogUnusableArguments();

                outputs.Add(ResponseItem.CreateFunctionCallOutputItem(
                    call.CallId, KnowledgeSearchTool.RenderUnusableCall()));

                continue;
            }

            Result<IReadOnlyList<ChunkSearchResult>> searchResult = await _searchTool
                .SearchAsync(query, run.MaxSources, cancellationToken)
                .ConfigureAwait(false);

            if (searchResult.IsFailure)
            {
                // Passed through with its own code, as everywhere else in this
                // solution: the caller learns that embedding or search failed,
                // not merely that "the agent" did.
                return Result.Failure<IReadOnlyList<ResponseItem>>(searchResult.Error);
            }

            run.SearchCount++;

            outputs.Add(ResponseItem.CreateFunctionCallOutputItem(
                call.CallId, _searchTool.RenderResult(run.Register(searchResult.Value))));
        }

        return outputs;
    }

    /// <summary>
    /// Sends one turn of the exchange, with retry and the failure taxonomy.
    /// </summary>
    /// <remarks>
    /// Every turn goes through here, including the ones that only carry tool
    /// output, so a 429 midway through a run is retried on the same terms as the
    /// first call rather than losing the work already done.
    /// </remarks>
    private async Task<Result<ResponseResult>> CreateResponseAsync(
        ProjectResponsesClient client,
        IReadOnlyList<ResponseItem> input,
        string? previousResponseId,
        CancellationToken cancellationToken)
    {
        try
        {
            ResponseResult response = await _resiliencePipeline.ExecuteAsync(
                async token =>
                {
                    ClientResult<ResponseResult> result = await client
                        .CreateResponseAsync(input, previousResponseId, token)
                        .ConfigureAwait(false);

                    return result.Value;
                },
                cancellationToken).ConfigureAwait(false);

            if (response.Error is not null)
            {
                LogRunReportedError(response.Error.Code.ToString(), response.Error.Message);
                return Result.Failure<ResponseResult>(AgentErrors.RunFailed);
            }

            return response;
        }
        catch (ClientResultException exception) when (exception.Status == 429)
        {
            LogRateLimited(exception);
            return Result.Failure<ResponseResult>(AgentErrors.RateLimited);
        }
        catch (ClientResultException exception)
        {
            LogRunFailed(exception, exception.Status);
            return Result.Failure<ResponseResult>(AgentErrors.RunFailed);
        }
        // Covers CredentialUnavailableException too, which derives from it.
        catch (AuthenticationFailedException exception)
        {
            LogAuthenticationFailed(exception);
            return Result.Failure<ResponseResult>(AgentErrors.AuthenticationFailed);
        }
        catch (HttpRequestException exception)
        {
            LogTransportFailed(exception);
            return Result.Failure<ResponseResult>(AgentErrors.RunFailed);
        }
    }

    /// <summary>
    /// Returns the responses client, resolving the agent version on first use.
    /// </summary>
    private async Task<Result<ProjectResponsesClient>> GetClientAsync(CancellationToken cancellationToken)
    {
        ProjectResponsesClient? client = _client;

        if (client is not null)
        {
            return client;
        }

        await _clientLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_client is not null)
            {
                return _client;
            }

            Result<string> versionResult = await _provisioner
                .ResolveVersionAsync(cancellationToken)
                .ConfigureAwait(false);

            if (versionResult.IsFailure)
            {
                return Result.Failure<ProjectResponsesClient>(versionResult.Error);
            }

            _client = _clientFactory(new AgentReference(_options.Name, versionResult.Value));

            return _client;
        }
        finally
        {
            _clientLock.Release();
        }
    }

    /// <summary>
    /// The mutable state of one question: its evidence, its counters, its bill.
    /// </summary>
    /// <remarks>
    /// A type rather than six locals threaded through three methods, and scoped to
    /// a single call rather than to the adapter — which is what keeps a singleton
    /// adapter safe to use concurrently.
    /// </remarks>
    private sealed class AgentRun(int maxSources)
    {
        private readonly Dictionary<Guid, int> _references = [];
        private readonly List<ChunkSearchResult> _sources = [];
        private bool _usageReported;

        public int MaxSources { get; } = maxSources;

        /// <summary>Searches actually run, across every round.</summary>
        public int SearchCount { get; set; }

        /// <summary>Rounds of tool calls answered, which the budget bounds.</summary>
        public int SearchRounds { get; set; }

        public int TotalTokens { get; private set; }

        private int PromptTokens { get; set; }

        private int CompletionTokens { get; set; }

        public IReadOnlyList<ChunkSearchResult> Sources => _sources;

        /// <summary>
        /// Assigns each passage its reference number, collapsing repeats.
        /// </summary>
        /// <remarks>
        /// A passage seen in an earlier search keeps the number it was given then.
        /// Renumbering it would put two numbers on one source, and a reader
        /// checking the answer's citations would find the same paragraph cited
        /// twice as though two documents agreed.
        /// </remarks>
        public List<(int Reference, ChunkSearchResult Chunk)> Register(
            IReadOnlyList<ChunkSearchResult> chunks)
        {
            var numbered = new List<(int, ChunkSearchResult)>(chunks.Count);

            foreach (ChunkSearchResult chunk in chunks)
            {
                if (!_references.TryGetValue(chunk.ChunkId, out int reference))
                {
                    _sources.Add(chunk);
                    reference = _sources.Count;
                    _references[chunk.ChunkId] = reference;
                }

                numbered.Add((reference, chunk));
            }

            return numbered;
        }

        /// <summary>Adds one turn's usage to the run's total.</summary>
        public void AddUsage(ResponseTokenUsage? usage)
        {
            if (usage is null)
            {
                return;
            }

            _usageReported = true;
            PromptTokens += usage.InputTokenCount;
            CompletionTokens += usage.OutputTokenCount;
            TotalTokens += usage.TotalTokenCount;
        }

        /// <summary>
        /// The run's total cost, or null when the provider reported none.
        /// </summary>
        /// <remarks>
        /// Null rather than zeros, matching <see cref="ChatCompletionResult"/>:
        /// a zero token count is indistinguishable from a free call in whatever
        /// dashboard consumes it.
        /// </remarks>
        public TokenUsage? ToTokenUsage() =>
            _usageReported ? new TokenUsage(PromptTokens, CompletionTokens, TotalTokens) : null;
    }

    /// <summary>Releases the lock guarding first-use client construction.</summary>
    /// <remarks>
    /// A singleton is disposed when the container is, so this runs at shutdown. It
    /// exists because holding an undisposed <see cref="SemaphoreSlim"/> is a
    /// diagnosable leak rather than because the process is about to reclaim it
    /// anyway — the same reason the provisioner disposes its own.
    /// </remarks>
    public void Dispose() => _clientLock.Dispose();

    [LoggerMessage(
        EventId = 5320,
        Level = LogLevel.Information,
        Message = "Agent run finished with status {Status} after {SearchCount} searches using {TotalTokens} tokens.")]
    private partial void LogRunCompleted(ResponseStatus? status, int searchCount, int totalTokens);

    [LoggerMessage(
        EventId = 5321,
        Level = LogLevel.Error,
        Message = "Agent run failed with status {Status}.")]
    private partial void LogRunFailed(Exception exception, int status);

    [LoggerMessage(
        EventId = 5322,
        Level = LogLevel.Error,
        Message = "The agent is rate limited; retries were exhausted.")]
    private partial void LogRateLimited(Exception exception);

    [LoggerMessage(
        EventId = 5323,
        Level = LogLevel.Error,
        Message = "Failed to authenticate to Azure AI Foundry. Verify the managed identity and its RBAC role assignments.")]
    private partial void LogAuthenticationFailed(Exception exception);

    [LoggerMessage(
        EventId = 5324,
        Level = LogLevel.Error,
        Message = "Azure AI Foundry was unreachable after all retries were exhausted.")]
    private partial void LogTransportFailed(Exception exception);

    [LoggerMessage(
        EventId = 5325,
        Level = LogLevel.Error,
        Message = "The agent run reported error {ErrorCode}: {ErrorMessage}")]
    private partial void LogRunReportedError(string errorCode, string errorMessage);

    [LoggerMessage(
        EventId = 5326,
        Level = LogLevel.Warning,
        Message = "The agent reached its {MaxToolIterations}-round search budget after {SearchCount} searches " +
                  "and was asked to answer with what it had. Raise Azure:AiFoundry:Agent:MaxToolIterations if " +
                  "answers are consistently incomplete.")]
    private partial void LogSearchBudgetExhausted(int maxToolIterations, int searchCount);

    [LoggerMessage(
        EventId = 5327,
        Level = LogLevel.Warning,
        Message = "The agent requested {CallCount} further searches after its budget was spent; the run was stopped.")]
    private partial void LogIgnoredCallsAfterBudget(int callCount);

    [LoggerMessage(
        EventId = 5328,
        Level = LogLevel.Warning,
        Message = "The agent called unknown function {FunctionName}. The provisioned agent version declares a " +
                  "tool this build does not implement.")]
    private partial void LogUnknownFunction(string functionName);

    [LoggerMessage(
        EventId = 5329,
        Level = LogLevel.Warning,
        Message = "The agent called the search function without a usable query; it was asked to try again.")]
    private partial void LogUnusableArguments();

    [LoggerMessage(
        EventId = 5330,
        Level = LogLevel.Warning,
        Message = "The agent produced no text after {SearchCount} searches.")]
    private partial void LogNoAnswer(int searchCount);
}
