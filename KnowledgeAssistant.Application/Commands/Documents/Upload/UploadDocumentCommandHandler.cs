using FluentValidation;
using FluentValidation.Results;
using KnowledgeAssistant.Application.Abstractions;
using KnowledgeAssistant.Application.Common;
using KnowledgeAssistant.Application.Interfaces;
using KnowledgeAssistant.Domain.Common;
using Microsoft.Extensions.Logging;

namespace KnowledgeAssistant.Application.Commands.Documents.Upload;

/// <summary>
/// Ingests an uploaded document: stores it, then makes it retrievable.
/// </summary>
/// <remarks>
/// <para>
/// <b>The pipeline.</b> Store the bytes, read them back, extract text, chunk it,
/// embed every chunk, index the chunks, and finally record the document itself:
/// </para>
/// <code>
/// upload → download → chunk → embed → index chunks → index document
/// </code>
/// <para>
/// <b>This class orchestrates and computes nothing.</b> Every step is a call to a
/// port that owns its own rules, its own retries, and its own failure taxonomy.
/// The handler's entire job is sequencing, stopping on the first failure, and
/// joining two collections. That is what keeps each service independently
/// testable and keeps this file readable as a description of the use case.
/// </para>
/// <para>
/// <b>Service errors pass through unaltered.</b> A chunking failure surfaces as
/// <c>Chunking.*</c>, an embedding failure as <c>Embedding.*</c>. Re-wrapping
/// them in a generic ingestion error would be tidier and strictly worse: the
/// error code is what tells an operator which stage broke, and it is the only
/// such signal that reaches the caller.
/// </para>
///
/// <para>
/// <b>Why the document index is written last.</b> It was written immediately
/// after upload in Sprint 4, before there was anything else to do. Moving it to
/// the end turns it into the <i>ingestion-complete marker</i>, and that single
/// change is what makes partial failures tractable:
/// </para>
/// <list type="bullet">
/// <item>
/// A blob with an entry in the document index was fully ingested — text
/// extracted, chunks embedded, vectors indexed.
/// </item>
/// <item>
/// A blob <b>without</b> one is an incomplete ingestion, whatever stage it
/// stopped at.
/// </item>
/// </list>
/// <para>
/// So the orphan question has an operational answer rather than a shrug. A
/// reconciliation pass lists blobs, reads the <c>documentId</c> that the storage
/// adapter writes into blob metadata, checks the document index for it, and
/// re-drives ingestion for anything missing. It converges rather than
/// duplicating, because every step is idempotent: the blob name derives from the
/// document id, chunk ids derive from the document id and position, and both
/// indexes upsert. Re-running a half-finished ingestion is safe by construction.
/// </para>
/// <para>
/// <b>Orphaned blobs are never deleted.</b> The blob is the input, and the only
/// artefact ingestion can be replayed from. Deleting it to tidy up a retryable
/// failure destroys the upload the user actually made, and a compensating delete
/// that itself fails leaves the original problem plus a half-executed rollback.
/// The failure is reported, the orphan is logged with everything needed to find
/// it, and the bytes are kept.
/// </para>
/// <para>
/// <b>What this design does not solve.</b> Ingestion is synchronous, so a caller
/// waits through extraction, embedding, and two index writes, and a client that
/// disconnects cancels work that was nearly complete. The real fix is to make
/// this handler store the blob and enqueue, and to run the pipeline from the
/// queue — at which point the reconciliation rule above becomes the queue's retry
/// policy. That is a change of shape, not a change of logic: every step below
/// moves unmodified.
/// </para>
///
/// <para>
/// <b>Why validation is invoked here rather than by a pipeline behaviour.</b>
/// A <c>ValidationBehavior</c> is the right destination and the approved
/// architecture reserves a folder for it — but building a generic,
/// <c>Result</c>-returning behaviour requires reflection to construct
/// <c>Result&lt;T&gt;.Failure</c> for an open <c>TResponse</c>, and that
/// machinery cannot be justified by a single slice. The moment these six lines
/// appear in a third handler, promote them.
/// </para>
/// <para>
/// <b>Why <see cref="TimeProvider"/> rather than <c>DateTimeOffset.UtcNow</c>.</b>
/// A static clock read is untestable, and it is used here for stage timings as
/// well as the timestamp — so a test can assert both exactly by substituting
/// <c>FakeTimeProvider</c>.
/// </para>
/// <para>
/// <b>Why the class is <c>internal</c>.</b> Nothing outside this assembly should
/// construct or call a handler directly; the only legitimate entry point is
/// sending the command through the mediator. MediatR still discovers it, because
/// assembly scanning sees internal types.
/// </para>
/// </remarks>
internal sealed partial class UploadDocumentCommandHandler
    : ICommandHandler<UploadDocumentCommand, UploadDocumentResponse>
{
    /// <summary>
    /// The content type recorded against the stored blob.
    /// </summary>
    /// <remarks>
    /// Deliberately not the client's declared <c>ContentType</c>. That value is
    /// attacker-controlled, and persisting it means whatever the caller sent is
    /// what the service will later hand back in a <c>Content-Type</c> header — a
    /// stored cross-site-scripting vector the day a download endpoint exists.
    /// <para>
    /// The response still echoes what the client declared, because that is a
    /// faithful report of the request. This constant is what was stored.
    /// </para>
    /// </remarks>
    private const string StoredContentType = "application/pdf";

    private readonly IValidator<UploadDocumentCommand> _validator;
    private readonly IBlobStorageService _blobStorageService;
    private readonly IDocumentChunkingService _chunkingService;
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorIndexService _vectorIndexService;
    private readonly IAzureSearchService _searchService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<UploadDocumentCommandHandler> _logger;

    /// <summary>Initialises the handler.</summary>
    /// <remarks>
    /// Six collaborators is a lot, and it is the honest count for a pipeline with
    /// six stages. Collapsing them behind one "ingestion service" would hide the
    /// dependency rather than remove it, and would put the sequencing somewhere a
    /// reader of this slice could not see it.
    /// </remarks>
    public UploadDocumentCommandHandler(
        IValidator<UploadDocumentCommand> validator,
        IBlobStorageService blobStorageService,
        IDocumentChunkingService chunkingService,
        IEmbeddingService embeddingService,
        IVectorIndexService vectorIndexService,
        IAzureSearchService searchService,
        TimeProvider timeProvider,
        ILogger<UploadDocumentCommandHandler> logger)
    {
        _validator = validator;
        _blobStorageService = blobStorageService;
        _chunkingService = chunkingService;
        _embeddingService = embeddingService;
        _vectorIndexService = vectorIndexService;
        _searchService = searchService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Runs the ingestion pipeline for one uploaded document.</summary>
    public async Task<Result<UploadDocumentResponse>> Handle(
        UploadDocumentCommand request,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator
            .ValidateAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (!validationResult.IsValid)
        {
            return Result.Failure<UploadDocumentResponse>(validationResult.ToValidationError());
        }

        // Version 7 GUIDs embed a timestamp in their high bits, so they sort in
        // creation order. Generated before the upload because the blob name, and
        // in turn every chunk id, derives from it.
        var documentId = Guid.CreateVersion7();

        long ingestionStarted = _timeProvider.GetTimestamp();

        LogIngestionStarted(documentId, request.FileName, request.SizeInBytes);

        // ---- Stage 1: store the bytes -------------------------------------
        // Nothing before this point has side effects, so a failure here leaves
        // nothing behind.
        long stageStarted = _timeProvider.GetTimestamp();

        Result<BlobUploadResult> uploadResult = await _blobStorageService
            .UploadAsync(documentId, request.FileName, StoredContentType, request.Content, cancellationToken)
            .ConfigureAwait(false);

        if (uploadResult.IsFailure)
        {
            LogStageFailed(documentId, "Upload", uploadResult.Error.Code);
            return Result.Failure<UploadDocumentResponse>(uploadResult.Error);
        }

        BlobUploadResult blob = uploadResult.Value;

        // Elapsed times are hoisted into locals rather than computed inside the
        // logging call: the source-generated methods short-circuit when the level
        // is disabled, and an analyzer rightly objects to work being handed to a
        // call that may discard it. The computation is cheap enough to do
        // unconditionally.
        double uploadElapsedMs = Elapsed(stageStarted);

        LogUploadCompleted(documentId, blob.BlobName, blob.SizeInBytes, uploadElapsedMs);

        // Read once and reused for the indexed value and the response, so the two
        // cannot disagree about when this document was accepted.
        DateTimeOffset receivedAtUtc = _timeProvider.GetUtcNow();

        // ---- Stages 2-6 ----------------------------------------------------
        // Everything past this point can leave an orphaned blob behind.
        Result<int> ingestionResult = await IngestStoredDocumentAsync(
                documentId, request.FileName, blob, receivedAtUtc, cancellationToken)
            .ConfigureAwait(false);

        if (ingestionResult.IsFailure)
        {
            LogOrphanedBlob(documentId, blob.BlobName, ingestionResult.Error.Code);
            return Result.Failure<UploadDocumentResponse>(ingestionResult.Error);
        }

        double totalElapsedMs = Elapsed(ingestionStarted);

        LogIngestionCompleted(documentId, ingestionResult.Value, totalElapsedMs);

        return new UploadDocumentResponse(
            DocumentId: documentId,
            FileName: request.FileName,
            ContentType: request.ContentType,
            // The measured size from storage, not the client's declared length.
            SizeInBytes: blob.SizeInBytes,
            BlobName: blob.BlobName,
            ChunkCount: ingestionResult.Value,
            ReceivedAtUtc: receivedAtUtc);
    }

    /// <summary>
    /// Runs every stage that follows a successful upload.
    /// </summary>
    /// <returns>The number of chunks indexed, or the first failure.</returns>
    /// <remarks>
    /// Separated from <see cref="Handle"/> so that the boundary is explicit: a
    /// failure returned from here means bytes are in storage and the caller's
    /// request did not succeed. That is the exact condition the orphan log
    /// describes, and keeping it to one call site means it cannot be reported
    /// from some paths and forgotten on others.
    /// </remarks>
    private async Task<Result<int>> IngestStoredDocumentAsync(
        Guid documentId,
        string fileName,
        BlobUploadResult blob,
        DateTimeOffset receivedAtUtc,
        CancellationToken cancellationToken)
    {
        // ---- Stage 2: read the bytes back ---------------------------------
        long stageStarted = _timeProvider.GetTimestamp();

        Result<Stream> downloadResult = await _blobStorageService
            .DownloadAsync(blob.BlobName, cancellationToken)
            .ConfigureAwait(false);

        if (downloadResult.IsFailure)
        {
            LogStageFailed(documentId, "Download", downloadResult.Error.Code);
            return Result.Failure<int>(downloadResult.Error);
        }

        // The port hands ownership of the stream to this method, and this is the
        // only scope that holds it.
        await using Stream content = downloadResult.Value;

        double downloadElapsedMs = Elapsed(stageStarted);

        LogDownloadCompleted(documentId, blob.BlobName, downloadElapsedMs);

        // ---- Stages 3 and 4: extract text, then chunk it ------------------
        // One call: which extractor runs, and how the text is split, are both
        // decisions the chunking service owns. Splitting them across two ports
        // would put an infrastructure choice into this sequence.
        stageStarted = _timeProvider.GetTimestamp();

        Result<IReadOnlyList<DocumentChunk>> chunkResult = await _chunkingService
            .ChunkAsync(documentId, content, cancellationToken)
            .ConfigureAwait(false);

        if (chunkResult.IsFailure)
        {
            LogStageFailed(documentId, "Chunking", chunkResult.Error.Code);
            return Result.Failure<int>(chunkResult.Error);
        }

        IReadOnlyList<DocumentChunk> chunks = chunkResult.Value;

        double chunkingElapsedMs = Elapsed(stageStarted);

        LogChunkingCompleted(documentId, chunks.Count, chunkingElapsedMs);

        // ---- Stage 5: embed every chunk -----------------------------------
        stageStarted = _timeProvider.GetTimestamp();

        Result<IReadOnlyList<ChunkEmbedding>> embeddingResult = await _embeddingService
            .GenerateEmbeddingsAsync(chunks, cancellationToken)
            .ConfigureAwait(false);

        if (embeddingResult.IsFailure)
        {
            LogStageFailed(documentId, "Embedding", embeddingResult.Error.Code);
            return Result.Failure<int>(embeddingResult.Error);
        }

        double embeddingElapsedMs = Elapsed(stageStarted);

        LogEmbeddingCompleted(documentId, embeddingResult.Value.Count, embeddingElapsedMs);

        Result<VectorIndexChunk[]> joinResult = JoinChunksWithVectors(documentId, chunks, embeddingResult.Value);

        if (joinResult.IsFailure)
        {
            return Result.Failure<int>(joinResult.Error);
        }

        // ---- Stage 6: index the chunks ------------------------------------
        stageStarted = _timeProvider.GetTimestamp();

        Result vectorIndexResult = await _vectorIndexService
            .IndexChunksAsync(
                new VectorIndexRequest(documentId, blob.BlobUri, receivedAtUtc, joinResult.Value),
                cancellationToken)
            .ConfigureAwait(false);

        if (vectorIndexResult.IsFailure)
        {
            LogStageFailed(documentId, "VectorIndexing", vectorIndexResult.Error.Code);
            return Result.Failure<int>(vectorIndexResult.Error);
        }

        double vectorIndexElapsedMs = Elapsed(stageStarted);

        LogVectorIndexingCompleted(documentId, joinResult.Value.Length, vectorIndexElapsedMs);

        // ---- Stage 7: record the document ---------------------------------
        // Last on purpose. Its presence is what marks this document as fully
        // ingested; see the class remarks.
        stageStarted = _timeProvider.GetTimestamp();

        Result documentIndexResult = await _searchService
            .IndexDocumentAsync(
                new DocumentIndexRequest(
                    DocumentId: documentId,
                    BlobName: blob.BlobName,
                    OriginalFileName: fileName,
                    BlobUri: blob.BlobUri,
                    UploadedAt: receivedAtUtc),
                cancellationToken)
            .ConfigureAwait(false);

        if (documentIndexResult.IsFailure)
        {
            LogStageFailed(documentId, "DocumentIndexing", documentIndexResult.Error.Code);
            return Result.Failure<int>(documentIndexResult.Error);
        }

        double documentIndexElapsedMs = Elapsed(stageStarted);

        LogDocumentIndexingCompleted(documentId, documentIndexElapsedMs);

        return chunks.Count;
    }

    /// <summary>
    /// Pairs each chunk with its vector.
    /// </summary>
    /// <remarks>
    /// <b>Matched by chunk id, not by position.</b> The embedding port does
    /// guarantee order and count, and enforces it on its own side — but this is a
    /// join, and a join is where a correspondence should be established rather
    /// than assumed. A positional pairing would go on working silently if that
    /// guarantee ever weakened, and its failure mode is the worst kind: every
    /// chunk indexed against another chunk's vector, retrieval returning
    /// confidently wrong passages, and no error anywhere.
    /// </remarks>
    private Result<VectorIndexChunk[]> JoinChunksWithVectors(
        Guid documentId,
        IReadOnlyList<DocumentChunk> chunks,
        IReadOnlyList<ChunkEmbedding> embeddings)
    {
        var vectorsByChunkId = new Dictionary<Guid, ReadOnlyMemory<float>>(embeddings.Count);

        foreach (ChunkEmbedding embedding in embeddings)
        {
            vectorsByChunkId[embedding.ChunkId] = embedding.Vector;
        }

        var indexChunks = new VectorIndexChunk[chunks.Count];

        for (int index = 0; index < chunks.Count; index++)
        {
            DocumentChunk chunk = chunks[index];

            if (!vectorsByChunkId.TryGetValue(chunk.ChunkId, out ReadOnlyMemory<float> vector))
            {
                LogEmbeddingMissing(documentId, chunk.ChunkId, chunk.ChunkOrder);
                return Result.Failure<VectorIndexChunk[]>(UploadDocumentErrors.EmbeddingMissing);
            }

            indexChunks[index] = new VectorIndexChunk(
                chunk.ChunkId,
                chunk.ChunkOrder,
                chunk.Text,
                vector);
        }

        return indexChunks;
    }

    /// <summary>Milliseconds elapsed since <paramref name="startingTimestamp"/>.</summary>
    /// <remarks>
    /// Measured through <see cref="TimeProvider"/> rather than
    /// <c>Stopwatch</c> so that stage timings are deterministic under a
    /// substituted clock, and so a test can assert them instead of tolerating
    /// them.
    /// </remarks>
    private double Elapsed(long startingTimestamp) =>
        _timeProvider.GetElapsedTime(startingTimestamp).TotalMilliseconds;

    // Structured logging for every stage. Each line carries the document id, so a
    // single ingestion can be reconstructed end to end from a log query, and an
    // elapsed time, so the slow stage is visible without a profiler.

    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Information,
        Message = "Ingestion started for document {DocumentId}: {FileName} ({SizeInBytes} bytes).")]
    private partial void LogIngestionStarted(Guid documentId, string fileName, long sizeInBytes);

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Information,
        Message = "Document {DocumentId} stored as {BlobName} ({SizeInBytes} bytes) in {ElapsedMs:F0}ms.")]
    private partial void LogUploadCompleted(Guid documentId, string blobName, long sizeInBytes, double elapsedMs);

    [LoggerMessage(
        EventId = 3002,
        Level = LogLevel.Information,
        Message = "Document {DocumentId} read back from {BlobName} in {ElapsedMs:F0}ms.")]
    private partial void LogDownloadCompleted(Guid documentId, string blobName, double elapsedMs);

    [LoggerMessage(
        EventId = 3003,
        Level = LogLevel.Information,
        Message = "Document {DocumentId} produced {ChunkCount} chunks in {ElapsedMs:F0}ms.")]
    private partial void LogChunkingCompleted(Guid documentId, int chunkCount, double elapsedMs);

    [LoggerMessage(
        EventId = 3004,
        Level = LogLevel.Information,
        Message = "Document {DocumentId} embedded {EmbeddingCount} chunks in {ElapsedMs:F0}ms.")]
    private partial void LogEmbeddingCompleted(Guid documentId, int embeddingCount, double elapsedMs);

    [LoggerMessage(
        EventId = 3005,
        Level = LogLevel.Information,
        Message = "Document {DocumentId} indexed {ChunkCount} chunks into the retrieval index in {ElapsedMs:F0}ms.")]
    private partial void LogVectorIndexingCompleted(Guid documentId, int chunkCount, double elapsedMs);

    [LoggerMessage(
        EventId = 3006,
        Level = LogLevel.Information,
        Message = "Document {DocumentId} recorded in the document index in {ElapsedMs:F0}ms.")]
    private partial void LogDocumentIndexingCompleted(Guid documentId, double elapsedMs);

    [LoggerMessage(
        EventId = 3007,
        Level = LogLevel.Information,
        Message = "Ingestion completed for document {DocumentId}: {ChunkCount} chunks in {ElapsedMs:F0}ms.")]
    private partial void LogIngestionCompleted(Guid documentId, int chunkCount, double elapsedMs);

    [LoggerMessage(
        EventId = 3008,
        Level = LogLevel.Warning,
        Message = "Ingestion of document {DocumentId} failed at stage {Stage} with {ErrorCode}.")]
    private partial void LogStageFailed(Guid documentId, string stage, string errorCode);

    [LoggerMessage(
        EventId = 3009,
        Level = LogLevel.Error,
        Message = "Document {DocumentId} is stored as {BlobName} but ingestion did not complete ({ErrorCode}). " +
                  "The blob has no document-index entry and is therefore an incomplete ingestion: " +
                  "re-drive it from the blob rather than deleting it. Every stage is idempotent, so re-running converges.")]
    private partial void LogOrphanedBlob(Guid documentId, string blobName, string errorCode);

    [LoggerMessage(
        EventId = 3010,
        Level = LogLevel.Error,
        Message = "Chunk {ChunkId} (order {ChunkOrder}) of document {DocumentId} has no embedding. " +
                  "The embedding service returned a set that does not cover its input.")]
    private partial void LogEmbeddingMissing(Guid documentId, Guid chunkId, int chunkOrder);
}
