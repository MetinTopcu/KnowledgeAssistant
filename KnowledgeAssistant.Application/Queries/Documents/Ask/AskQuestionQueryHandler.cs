using FluentValidation;
using FluentValidation.Results;
using KnowledgeAssistant.Application.Abstractions;
using KnowledgeAssistant.Application.Common;
using KnowledgeAssistant.Application.Interfaces;
using KnowledgeAssistant.Domain.Common;
using Microsoft.Extensions.Logging;

namespace KnowledgeAssistant.Application.Queries.Documents.Ask;

/// <summary>
/// Answers a question from the ingested corpus.
/// </summary>
/// <remarks>
/// <para>
/// <b>The pipeline.</b> Embed the question, find the closest chunks, build a
/// grounded prompt from them, and ask the model:
/// </para>
/// <code>
/// embed question → vector search → build prompt → chat completion → answer + citations
/// </code>
/// <para>
/// <b>This class orchestrates and computes nothing.</b> Embedding, search, and
/// completion are ports that own their own retries and failure taxonomies;
/// prompt construction is a pure function next door. What is left here is
/// sequencing, one early exit, and mapping results to citations.
/// </para>
/// <para>
/// <b>The question is embedded by the same service that embedded the corpus.</b>
/// Not for tidiness: vectors from different models are not comparable, and
/// comparing them produces a search that returns plausible-looking nonsense
/// rather than an error. Routing both through one port makes that structurally
/// true rather than something to remember.
/// </para>
/// <para>
/// <b>Retrieving nothing short-circuits the model.</b> With no sources the system
/// prompt leaves the model nothing to do but decline, so calling it would spend
/// money to be told what is already known — and would give an ungrounded answer
/// an opportunity to appear. The handler returns the honest answer directly, with
/// zero citations and a zero chunk count, as a success.
/// </para>
/// <para>
/// <b>Service errors pass through unaltered</b>, as in the ingestion handler: an
/// embedding failure surfaces as <c>Embedding.*</c>, a search failure as
/// <c>Search.*</c>, a completion failure as <c>Chat.*</c>. The error code is what
/// tells an operator which stage broke.
/// </para>
/// <para>
/// <b>Nothing here writes.</b> The query touches no blob and no index, so a
/// failure at any stage leaves no state to reconcile — the contrast with
/// ingestion, where every stage past the first can leave an orphan.
/// </para>
/// </remarks>
internal sealed partial class AskQuestionQueryHandler
    : IQueryHandler<AskQuestionQuery, AskQuestionResponse>
{
    private readonly IValidator<AskQuestionQuery> _validator;
    private readonly IEmbeddingService _embeddingService;
    private readonly IAzureSearchService _searchService;
    private readonly IChatService _chatService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AskQuestionQueryHandler> _logger;

    /// <summary>Initialises the handler.</summary>
    public AskQuestionQueryHandler(
        IValidator<AskQuestionQuery> validator,
        IEmbeddingService embeddingService,
        IAzureSearchService searchService,
        IChatService chatService,
        TimeProvider timeProvider,
        ILogger<AskQuestionQueryHandler> logger)
    {
        _validator = validator;
        _embeddingService = embeddingService;
        _searchService = searchService;
        _chatService = chatService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Runs the retrieval-augmented answer pipeline.</summary>
    public async Task<Result<AskQuestionResponse>> Handle(
        AskQuestionQuery request,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator
            .ValidateAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (!validationResult.IsValid)
        {
            return Result.Failure<AskQuestionResponse>(validationResult.ToValidationError());
        }

        long pipelineStarted = _timeProvider.GetTimestamp();

        // The question itself is never logged. It is user input, plausibly
        // sensitive, and its length and outcome are what an operator needs.
        LogQuestionReceived(request.Question.Length, request.TopK);

        // ---- Stage 1: embed the question ----------------------------------
        long stageStarted = _timeProvider.GetTimestamp();

        Result<ReadOnlyMemory<float>> embeddingResult = await _embeddingService
            .GenerateEmbeddingAsync(request.Question, cancellationToken)
            .ConfigureAwait(false);

        if (embeddingResult.IsFailure)
        {
            LogStageFailed("QuestionEmbedding", embeddingResult.Error.Code);
            return Result.Failure<AskQuestionResponse>(embeddingResult.Error);
        }

        double embeddingElapsedMs = Elapsed(stageStarted);

        LogQuestionEmbedded(embeddingResult.Value.Length, embeddingElapsedMs);

        // ---- Stage 2: retrieve the closest chunks -------------------------
        stageStarted = _timeProvider.GetTimestamp();

        Result<IReadOnlyList<ChunkSearchResult>> searchResult = await _searchService
            .SearchChunksAsync(embeddingResult.Value, request.TopK, cancellationToken)
            .ConfigureAwait(false);

        if (searchResult.IsFailure)
        {
            LogStageFailed("VectorSearch", searchResult.Error.Code);
            return Result.Failure<AskQuestionResponse>(searchResult.Error);
        }

        IReadOnlyList<ChunkSearchResult> chunks = searchResult.Value;
        double searchElapsedMs = Elapsed(stageStarted);

        double topScore = TopScore(chunks);

        LogChunksRetrieved(chunks.Count, request.TopK, topScore, searchElapsedMs);

        if (chunks.Count == 0)
        {
            // A corpus with nothing relevant is an answer, not a fault. Returning
            // it here avoids paying for a completion whose only honest output is
            // this same sentence.
            double emptyElapsedMs = Elapsed(pipelineStarted);

            LogAnsweredWithoutEvidence(emptyElapsedMs);

            return new AskQuestionResponse(
                Answer: GroundedPromptBuilder.NoEvidenceAnswer,
                Citations: [],
                RetrievedChunkCount: 0,
                TokenUsage: null,
                AnsweredAtUtc: _timeProvider.GetUtcNow());
        }

        // ---- Stage 3: build the grounded prompt ---------------------------
        // Pure and synchronous; not timed, because there is nothing here that can
        // be slow and nothing that can fail.
        IReadOnlyList<ChatMessage> messages = GroundedPromptBuilder.Build(request.Question, chunks);

        // The prompt may hold fewer chunks than were retrieved, if the context
        // budget bound first. Citations are numbered from what was actually sent,
        // so they cannot credit the answer to text the model never saw.
        int groundedCount = GroundedPromptBuilder.CountChunksThatFit(chunks);

        if (groundedCount < chunks.Count)
        {
            LogContextTruncated(chunks.Count, groundedCount, GroundedPromptBuilder.MaxContextCharacters);
        }

        // ---- Stage 4: ask the model ---------------------------------------
        stageStarted = _timeProvider.GetTimestamp();

        Result<ChatCompletionResult> completionResult = await _chatService
            .CompleteAsync(messages, cancellationToken)
            .ConfigureAwait(false);

        if (completionResult.IsFailure)
        {
            LogStageFailed("ChatCompletion", completionResult.Error.Code);
            return Result.Failure<AskQuestionResponse>(completionResult.Error);
        }

        double completionElapsedMs = Elapsed(stageStarted);
        ChatCompletionResult completion = completionResult.Value;

        LogAnswerGenerated(
            completion.Content.Length,
            completion.Usage?.TotalTokens ?? 0,
            completionElapsedMs);

        double totalElapsedMs = Elapsed(pipelineStarted);

        LogQuestionAnswered(groundedCount, completion.Usage?.TotalTokens ?? 0, totalElapsedMs);

        return new AskQuestionResponse(
            Answer: completion.Content,
            Citations: BuildCitations(chunks, groundedCount),
            RetrievedChunkCount: chunks.Count,
            TokenUsage: completion.Usage,
            AnsweredAtUtc: _timeProvider.GetUtcNow());
    }

    /// <summary>
    /// Numbers the grounding chunks exactly as the prompt numbered them.
    /// </summary>
    /// <remarks>
    /// The reference number is a one-based index into the same ordered list the
    /// prompt builder walked, which is what makes a <c>[2]</c> in the answer
    /// resolve to the second citation here. Deriving both from one ordering is
    /// the only thing keeping that promise; two independent numberings would
    /// drift the first time either side changed.
    /// </remarks>
    private static AnswerCitation[] BuildCitations(
        IReadOnlyList<ChunkSearchResult> chunks,
        int groundedCount)
    {
        var citations = new AnswerCitation[groundedCount];

        for (int index = 0; index < groundedCount; index++)
        {
            ChunkSearchResult chunk = chunks[index];

            citations[index] = new AnswerCitation(
                ReferenceNumber: index + 1,
                ChunkId: chunk.ChunkId,
                DocumentId: chunk.DocumentId,
                ChunkOrder: chunk.ChunkOrder,
                Text: chunk.Text,
                BlobUri: chunk.BlobUri,
                Score: chunk.Score);
        }

        return citations;
    }

    private static double TopScore(IReadOnlyList<ChunkSearchResult> chunks) =>
        chunks.Count > 0 ? chunks[0].Score : 0;

    /// <summary>Milliseconds elapsed since <paramref name="startingTimestamp"/>.</summary>
    private double Elapsed(long startingTimestamp) =>
        _timeProvider.GetElapsedTime(startingTimestamp).TotalMilliseconds;

    // Structured logging for every stage. The question and the answer are never
    // logged — they are user content — so each line carries shape and timing
    // instead: enough to find a slow or empty-handed stage, and nothing that
    // turns the log into a store of what people asked.

    [LoggerMessage(
        EventId = 7000,
        Level = LogLevel.Information,
        Message = "Question received ({QuestionLength} characters, topK {TopK}).")]
    private partial void LogQuestionReceived(int questionLength, int topK);

    [LoggerMessage(
        EventId = 7001,
        Level = LogLevel.Information,
        Message = "Question embedded to {Dimensions} dimensions in {ElapsedMs:F0}ms.")]
    private partial void LogQuestionEmbedded(int dimensions, double elapsedMs);

    [LoggerMessage(
        EventId = 7002,
        Level = LogLevel.Information,
        Message = "Retrieved {ChunkCount} of {TopK} requested chunks (top score {TopScore:F4}) in {ElapsedMs:F0}ms.")]
    private partial void LogChunksRetrieved(int chunkCount, int topK, double topScore, double elapsedMs);

    [LoggerMessage(
        EventId = 7003,
        Level = LogLevel.Information,
        Message = "Answer generated ({AnswerLength} characters, {TotalTokens} tokens) in {ElapsedMs:F0}ms.")]
    private partial void LogAnswerGenerated(int answerLength, int totalTokens, double elapsedMs);

    [LoggerMessage(
        EventId = 7004,
        Level = LogLevel.Information,
        Message = "Question answered from {GroundedChunkCount} chunks, {TotalTokens} tokens, in {ElapsedMs:F0}ms.")]
    private partial void LogQuestionAnswered(int groundedChunkCount, int totalTokens, double elapsedMs);

    [LoggerMessage(
        EventId = 7005,
        Level = LogLevel.Information,
        Message = "No relevant chunks were found; answered without calling the model in {ElapsedMs:F0}ms.")]
    private partial void LogAnsweredWithoutEvidence(double elapsedMs);

    [LoggerMessage(
        EventId = 7006,
        Level = LogLevel.Warning,
        Message = "Context budget reached: {RetrievedCount} chunks retrieved but only {GroundedCount} fit within " +
                  "{MaxContextCharacters} characters. The least relevant chunks were dropped.")]
    private partial void LogContextTruncated(int retrievedCount, int groundedCount, int maxContextCharacters);

    [LoggerMessage(
        EventId = 7007,
        Level = LogLevel.Warning,
        Message = "Answering failed at stage {Stage} with {ErrorCode}.")]
    private partial void LogStageFailed(string stage, string errorCode);
}
