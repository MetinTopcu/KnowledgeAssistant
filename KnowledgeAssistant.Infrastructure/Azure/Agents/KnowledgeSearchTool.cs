using System.Buffers;
using System.Text.Json;
using KnowledgeAssistant.Application.Interfaces;
using KnowledgeAssistant.Domain.Common;
using Microsoft.Extensions.Logging;
using OpenAI.Responses;

namespace KnowledgeAssistant.Infrastructure.Azure.Agents;

/// <summary>
/// The single capability the agent is given: search this system's own corpus.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the whole of the tool surface, and it is deliberately one file.</b>
/// The function's name, its parameter schema, how a call is decoded, what runs,
/// and how the result is rendered back to the model all live here. Splitting them
/// is how the schema and the parser drift apart: the schema gains a field, the
/// parser never reads it, and the only symptom is an agent that quietly ignores
/// an argument it was told it could send.
/// </para>
/// <para>
/// <b>Why a function tool and not Foundry's Azure AI Search tool.</b> Foundry can
/// query a search index server-side, without this process being involved. That
/// was rejected: it would embed the agent's queries with the index's own
/// vectorizer rather than with <see cref="IEmbeddingService"/>, so the agent's
/// query vectors and the corpus vectors would come from two independently
/// configured models — and vectors from different models are not comparable, so
/// the failure appears as plausible but wrong results rather than as an error. It
/// would also bypass this solution's retry policy and its error taxonomy, leaving
/// a second retrieval implementation to keep in step with the first. Running the
/// search here means the agent retrieves through exactly the pipeline the
/// retrieval slice uses.
/// </para>
/// <para>
/// <b>Nothing outside this assembly knows a tool exists.</b> The Application layer
/// declares a port that answers questions; that the implementation happens to
/// achieve it by handing a model a callable function is an implementation detail
/// of this adapter, in the same way retry and batching are details of the others.
/// </para>
/// <para>
/// <b>The model chooses the query, never the budget.</b> The schema exposes one
/// argument — the search text. How many passages come back is the caller's
/// <c>MaxSources</c> bounded by configuration, because that is a cost decision and
/// the model has neither the information nor the incentive to make it.
/// </para>
/// </remarks>
internal sealed partial class KnowledgeSearchTool
{
    /// <summary>The name the agent calls this function by.</summary>
    /// <remarks>
    /// Part of the agent's provisioned definition, so changing it changes the
    /// definition fingerprint and causes a new agent version to be created. It is
    /// also what arrives on every tool call, and the dispatch below compares
    /// against it — a rename that missed one of the two would produce an agent
    /// that calls a function nothing answers.
    /// </remarks>
    internal const string FunctionName = "search_knowledge_base";

    /// <summary>What the agent is told the function does.</summary>
    /// <remarks>
    /// The model reads this to decide whether to call it, so it is prompt text
    /// rather than documentation. It says what the corpus is and, more usefully,
    /// what the search is like — semantic rather than keyword — because an agent
    /// that believes it is driving a keyword index writes quoted boolean queries
    /// and retrieves nothing.
    /// </remarks>
    internal const string FunctionDescription =
        "Search the organisation's indexed documents for passages relevant to a question. " +
        "The search is semantic, so a natural-language description of what you are looking for " +
        "works better than keywords. Returns numbered passages, most relevant first. " +
        "Returns an empty list when the documents contain nothing relevant.";

    /// <summary>The JSON Schema describing the function's arguments.</summary>
    /// <remarks>
    /// <para>
    /// Written out as a literal rather than generated from a type. It is part of a
    /// wire contract with a model and part of the fingerprint that decides whether
    /// a new agent version is created, so it needs to be reviewable as text and
    /// byte-stable across builds — neither of which survives a serializer's
    /// property ordering being an implementation detail.
    /// </para>
    /// <para>
    /// <c>additionalProperties: false</c> with every property required is what
    /// strict mode demands; together they make the model's arguments structurally
    /// guaranteed rather than merely likely, which removes an entire class of
    /// defensive parsing below.
    /// </para>
    /// </remarks>
    internal const string ParameterSchema =
        """
        {"type":"object","properties":{"query":{"type":"string","description":"What to look for, phrased as a natural-language description rather than keywords."}},"required":["query"],"additionalProperties":false}
        """;

    private readonly IEmbeddingService _embeddingService;
    private readonly IAzureSearchService _searchService;
    private readonly FoundryAgentOptions _options;
    private readonly ILogger<KnowledgeSearchTool> _logger;

    /// <summary>Initialises the tool over the ports it retrieves through.</summary>
    public KnowledgeSearchTool(
        IEmbeddingService embeddingService,
        IAzureSearchService searchService,
        FoundryAgentOptions options,
        ILogger<KnowledgeSearchTool> logger)
    {
        ArgumentNullException.ThrowIfNull(embeddingService);
        ArgumentNullException.ThrowIfNull(searchService);
        ArgumentNullException.ThrowIfNull(options);

        _embeddingService = embeddingService;
        _searchService = searchService;
        _options = options;
        _logger = logger;
    }

    /// <summary>Builds the tool definition attached to the agent.</summary>
    /// <remarks>
    /// Strict mode on. Without it the model may omit <c>query</c> or send it under
    /// another name, and the resulting failure surfaces as an agent that searched
    /// for nothing — which reads like a retrieval problem and is not one.
    /// </remarks>
    internal static ResponseTool CreateDefinition() =>
        ResponseTool.CreateFunctionTool(
            functionName: FunctionName,
            functionParameters: BinaryData.FromString(ParameterSchema),
            strictModeEnabled: true,
            functionDescription: FunctionDescription);

    /// <summary>
    /// Reads the search text out of a tool call's arguments.
    /// </summary>
    /// <returns>
    /// The query, or <see langword="null"/> when the arguments were unusable.
    /// </returns>
    /// <remarks>
    /// Strict mode makes malformed arguments close to impossible, so this returns
    /// null rather than throwing: a single unusable call is worth reporting back to
    /// the agent — which can then try again — rather than failing a request that is
    /// otherwise proceeding normally.
    /// </remarks>
    internal static string? TryReadQuery(BinaryData? arguments)
    {
        if (arguments is null)
        {
            return null;
        }

        ReadOnlyMemory<byte> bytes = arguments.ToMemory();

        if (bytes.IsEmpty)
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes);

            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("query", out JsonElement query) ||
                query.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            string? text = query.GetString();

            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Runs one search through this system's retrieval pipeline.
    /// </summary>
    /// <param name="query">The search text the agent supplied.</param>
    /// <param name="maxResults">The caller's ceiling on passages returned.</param>
    /// <param name="cancellationToken">Cancelled when the caller disconnects.</param>
    /// <returns>The matching passages in rank order, possibly empty, or a failure.</returns>
    /// <remarks>
    /// <para>
    /// <b>The same two ports the retrieval slice uses, in the same order.</b> That
    /// is what "reuse the RAG pipeline" means concretely: the query is embedded by
    /// the service that embedded the corpus, so the vectors are comparable, and the
    /// search runs against the same index with the same retry policy and the same
    /// error taxonomy. Nothing about retrieval is reimplemented here — this method
    /// sequences two calls and does no work of its own.
    /// </para>
    /// <para>
    /// <b>A retrieval failure fails the run rather than being reported to the
    /// model.</b> Handing the agent "the search is broken" invites it to answer
    /// from prior knowledge instead, producing a confident ungrounded answer at
    /// precisely the moment the system knows it cannot ground one. An empty result
    /// is different and <i>is</i> reported: nothing relevant in the corpus is a
    /// fact the agent should reason about.
    /// </para>
    /// </remarks>
    internal async Task<Result<IReadOnlyList<ChunkSearchResult>>> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken)
    {
        Result<ReadOnlyMemory<float>> embeddingResult = await _embeddingService
            .GenerateEmbeddingAsync(query, cancellationToken)
            .ConfigureAwait(false);

        if (embeddingResult.IsFailure)
        {
            LogSearchFailed("QueryEmbedding", embeddingResult.Error.Code);
            return Result.Failure<IReadOnlyList<ChunkSearchResult>>(embeddingResult.Error);
        }

        Result<IReadOnlyList<ChunkSearchResult>> searchResult = await _searchService
            .SearchChunksAsync(embeddingResult.Value, maxResults, cancellationToken)
            .ConfigureAwait(false);

        if (searchResult.IsFailure)
        {
            LogSearchFailed("VectorSearch", searchResult.Error.Code);
            return searchResult;
        }

        // The query text is never logged. It is derived from the user's question,
        // is plausibly sensitive, and its length and yield are what an operator
        // needs.
        LogSearchCompleted(query.Length, searchResult.Value.Count, maxResults);

        return searchResult;
    }

    /// <summary>
    /// Renders retrieved passages as the function's return value.
    /// </summary>
    /// <param name="sources">
    /// The passages with the reference numbers already assigned to them.
    /// </param>
    /// <returns>The JSON the model receives as the tool's output.</returns>
    /// <remarks>
    /// <para>
    /// <b>Reference numbers are supplied by the caller, not minted here.</b> An
    /// agent may search several times, and numbering each result set from one
    /// would produce two different passages both called <c>[1]</c> — so every
    /// citation in the answer would be ambiguous, and the response's citation list
    /// could not be reconciled with the answer text at all. The adapter owns one
    /// counter across the whole run for that reason.
    /// </para>
    /// <para>
    /// <b>Only what the model needs is sent.</b> Chunk ids, document ids, and blob
    /// URIs are omitted: the model cannot use them, they are billed as prompt
    /// tokens on every subsequent turn of the run, and the reference number already
    /// joins each passage back to the full record the response returns. What is
    /// left is the number, the score, and the text.
    /// </para>
    /// <para>
    /// <b>The character budget trims from the end</b>, which drops the least
    /// relevant passages because the list arrives in rank order.
    /// </para>
    /// </remarks>
    internal string RenderResult(IReadOnlyList<(int Reference, ChunkSearchResult Chunk)> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("sources");

            int usedCharacters = 0;

            foreach ((int reference, ChunkSearchResult chunk) in sources)
            {
                if (usedCharacters + chunk.Text.Length > _options.MaxSearchResultCharacters)
                {
                    break;
                }

                usedCharacters += chunk.Text.Length;

                writer.WriteStartObject();
                writer.WriteNumber("ref", reference);
                writer.WriteNumber("score", chunk.Score);
                writer.WriteString("text", chunk.Text);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    /// The output returned for a tool call whose arguments could not be read.
    /// </summary>
    /// <remarks>
    /// Deliberately shaped as a normal, empty result with a note rather than as an
    /// error object. The agent's instructions already tell it to search again with
    /// different wording when a search yields nothing, so this routes an
    /// unparseable call into behaviour the model has been told how to handle.
    /// </remarks>
    internal static string RenderUnusableCall() =>
        """{"sources":[],"note":"The search could not be run because no query was supplied. Call the function again with a query."}""";

    /// <summary>
    /// The output returned once the run has spent its search budget.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sent in place of results rather than by abandoning the run. The Responses
    /// protocol requires every tool call to be answered before the exchange can
    /// continue, so simply stopping would leave the agent with an unanswered call
    /// and no opportunity to write anything — turning a bounded-but-usable answer
    /// into an empty one.
    /// </para>
    /// <para>
    /// The note tells the agent plainly that searching is over and that it should
    /// answer from what it already has, including saying what it could not find.
    /// That is a better outcome for the caller than either an error or an
    /// unacknowledged truncation.
    /// </para>
    /// </remarks>
    internal static string RenderBudgetExhausted() =>
        """{"sources":[],"note":"No further searches are permitted for this question. Answer now using the passages you have already retrieved, and state plainly what you were unable to find."}""";

    /// <summary>
    /// The output returned for a call to a function this agent does not implement.
    /// </summary>
    /// <remarks>
    /// Reachable only when a provisioned agent version declares a tool this build
    /// does not answer — which happens when a version was pinned from a newer
    /// definition, or edited in the portal. Reported to the agent rather than
    /// thrown, so a stale pin degrades to an agent that searches less rather than
    /// to an endpoint that returns 500 for every question.
    /// </remarks>
    internal static string RenderUnknownFunction() =>
        """{"sources":[],"note":"That function is not available. Use the document search function instead."}""";

    [LoggerMessage(
        EventId = 5300,
        Level = LogLevel.Information,
        Message = "Agent search over {QueryLength} characters returned {ResultCount} of up to {MaxResults} passages.")]
    private partial void LogSearchCompleted(int queryLength, int resultCount, int maxResults);

    [LoggerMessage(
        EventId = 5301,
        Level = LogLevel.Warning,
        Message = "Agent search failed at stage {Stage} with {ErrorCode}.")]
    private partial void LogSearchFailed(string stage, string errorCode);
}
