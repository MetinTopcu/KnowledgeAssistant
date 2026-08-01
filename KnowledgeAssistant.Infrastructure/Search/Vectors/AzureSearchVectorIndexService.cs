using System.Globalization;
using Azure;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using KnowledgeAssistant.Application.Interfaces;
using KnowledgeAssistant.Domain.Common;
using KnowledgeAssistant.Infrastructure.Azure.OpenAI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAssistant.Infrastructure.Search.Vectors;

/// <summary>
/// Writes document chunks and their vectors into an Azure AI Search index.
/// </summary>
/// <remarks>
/// <para>
/// <b>Registered as a singleton</b>, like the document-level adapter: the SDK
/// clients are thread-safe and expensive to construct, and the index-existence
/// cache below depends on this lifetime.
/// </para>
/// <para>
/// <b>It writes to a different index than <c>AzureSearchService</c>.</b> That one
/// is keyed by document and answers "which documents exist"; this one is keyed by
/// chunk and answers "which passages match". Two indexes rather than one because
/// a single index has a single key, and these need different ones.
/// </para>
/// </remarks>
internal sealed partial class AzureSearchVectorIndexService : IVectorIndexService, IDisposable
{
    private readonly SearchIndexClient _indexClient;
    private readonly SearchClient _searchClient;
    private readonly AzureSearchOptions _searchOptions;
    private readonly AzureOpenAIOptions _openAIOptions;
    private readonly string _indexName;
    private readonly ILogger<AzureSearchVectorIndexService> _logger;

    // Guards the one-time index check. Not a Lazy<Task>, which would cache a
    // faulted task permanently and let one transient startup failure poison every
    // later write for the lifetime of the process.
    private readonly SemaphoreSlim _indexInitializationLock = new(1, 1);
    private volatile bool _indexVerified;

    /// <summary>Initialises the adapter.</summary>
    public AzureSearchVectorIndexService(
        SearchIndexClient indexClient,
        IOptions<AzureSearchOptions> searchOptions,
        IOptions<AzureOpenAIOptions> openAIOptions,
        ILogger<AzureSearchVectorIndexService> logger)
    {
        ArgumentNullException.ThrowIfNull(indexClient);
        ArgumentNullException.ThrowIfNull(searchOptions);
        ArgumentNullException.ThrowIfNull(openAIOptions);

        _indexClient = indexClient;
        _searchOptions = searchOptions.Value;
        _openAIOptions = openAIOptions.Value;
        _indexName = _searchOptions.ChunkIndexName;

        // Derived from the index client so both share one pipeline, one
        // credential, and one token cache.
        _searchClient = indexClient.GetSearchClient(_indexName);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result> IndexChunksAsync(
        VectorIndexRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Chunks.Count == 0)
        {
            // Nothing to write is not a failure. A document that produced no
            // chunks was already rejected upstream by the chunking service.
            return Result.Success();
        }

        Result indexResult = await EnsureIndexExistsAsync(cancellationToken).ConfigureAwait(false);

        if (indexResult.IsFailure)
        {
            return indexResult;
        }

        // Checked before anything is sent. The service would reject a wrong-sized
        // vector too, but only after the entire batch had been serialised and
        // uploaded, and its message names a field rather than the two numbers that
        // disagree.
        Result dimensionResult = ValidateDimensions(request);

        if (dimensionResult.IsFailure)
        {
            return dimensionResult;
        }

        int batchSize = _searchOptions.IndexingBatchSize;

        for (int offset = 0; offset < request.Chunks.Count; offset += batchSize)
        {
            int count = Math.Min(batchSize, request.Chunks.Count - offset);

            Result batchResult = await IndexBatchAsync(request, offset, count, cancellationToken)
                .ConfigureAwait(false);

            if (batchResult.IsFailure)
            {
                // Earlier batches stay in the index. That is stated in the port's
                // remarks and is safe: every write is an upsert keyed by chunk id,
                // so retrying the whole request converges instead of duplicating.
                LogPartialIndexing(request.DocumentId, offset, request.Chunks.Count);
                return batchResult;
            }
        }

        LogChunksIndexed(request.DocumentId, request.Chunks.Count, _indexName);

        return Result.Success();
    }

    /// <summary>
    /// Confirms every vector is the length the index was created for.
    /// </summary>
    private Result ValidateDimensions(VectorIndexRequest request)
    {
        int expected = _openAIOptions.EmbeddingDimensions;

        if (expected <= 0)
        {
            return Result.Success();
        }

        foreach (VectorIndexChunk chunk in request.Chunks)
        {
            if (chunk.Vector.Length != expected)
            {
                LogDimensionMismatch(chunk.ChunkId, expected, chunk.Vector.Length);
                return Result.Failure(VectorIndexErrors.DimensionMismatch);
            }
        }

        return Result.Success();
    }

    /// <summary>
    /// Writes one batch of chunks — metadata, text, and vector in a single
    /// upload per chunk.
    /// </summary>
    private async Task<Result> IndexBatchAsync(
        VectorIndexRequest request,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        var documents = new ChunkSearchDocument[count];
        string documentId = request.DocumentId.ToString();
        string blobUri = request.BlobUri.AbsoluteUri;

        for (int index = 0; index < count; index++)
        {
            VectorIndexChunk chunk = request.Chunks[offset + index];

            documents[index] = new ChunkSearchDocument
            {
                ChunkId = chunk.ChunkId.ToString(),
                DocumentId = documentId,
                ChunkOrder = chunk.ChunkOrder,
                ChunkText = chunk.Text,
                BlobUri = blobUri,
                UploadedAt = request.UploadedAt,

                // One copy, at the boundary. See ChunkSearchDocument's remarks.
                Embedding = chunk.Vector.ToArray(),
            };
        }

        Response<IndexDocumentsResult> response;

        try
        {
            // MergeOrUpload rather than Upload: chunk ids are derived from the
            // document and position, so re-indexing updates in place. It also
            // leaves untouched any field a later enrichment step may have written
            // to the same chunk.
            response = await _searchClient
                .MergeOrUploadDocumentsAsync(documents, options: null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (RequestFailedException exception)
        {
            LogIndexingFailed(exception, request.DocumentId, offset, count, exception.Status, exception.ErrorCode);
            return Result.Failure(VectorIndexErrors.IndexingFailed);
        }
        catch (AuthenticationFailedException exception)
        {
            LogAuthenticationFailed(exception);
            return Result.Failure(VectorIndexErrors.AuthenticationFailed);
        }
        catch (AggregateException exception)
        {
            // Retries exhausted at the transport level surface here, not as
            // RequestFailedException.
            LogTransportFailed(exception, _indexName);
            return Result.Failure(VectorIndexErrors.IndexingFailed);
        }

        return InspectResults(response.Value, count);
    }

    /// <summary>
    /// Checks the per-document outcomes inside an accepted response.
    /// </summary>
    /// <remarks>
    /// Azure AI Search reports success or failure per document inside a 200. A
    /// chunk can be rejected — throttled, too large, schema mismatch — while the
    /// HTTP call itself succeeds, so "no exception" is not "indexed".
    /// </remarks>
    private Result InspectResults(IndexDocumentsResult result, int expectedCount)
    {
        int succeeded = 0;
        IndexingResult? firstFailure = null;

        foreach (IndexingResult documentResult in result.Results)
        {
            if (documentResult.Succeeded)
            {
                succeeded++;
            }
            else
            {
                firstFailure ??= documentResult;
            }
        }

        if (firstFailure is not null || succeeded != expectedCount)
        {
            LogChunksRejected(
                expectedCount,
                succeeded,
                firstFailure?.Key ?? "(none)",
                firstFailure?.Status ?? 0,
                firstFailure?.ErrorMessage ?? "The service returned fewer results than documents sent.");

            return Result.Failure(VectorIndexErrors.ChunksRejected);
        }

        return Result.Success();
    }

    /// <summary>
    /// Creates the index on first use, then remembers that it exists.
    /// </summary>
    /// <remarks>
    /// <b>Create, never CreateOrUpdate</b> — the same rule as the document index,
    /// and it matters more here. Vector dimensions and the HNSW build parameters
    /// are immutable once an index exists, so an automatic update would either be
    /// rejected or, worse, partially applied. Changing this schema is a rebuild
    /// and a reload, performed deliberately.
    /// </remarks>
    private async Task<Result> EnsureIndexExistsAsync(CancellationToken cancellationToken)
    {
        if (_indexVerified)
        {
            return Result.Success();
        }

        await _indexInitializationLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_indexVerified)
            {
                return Result.Success();
            }

            try
            {
                await _indexClient.GetIndexAsync(_indexName, cancellationToken).ConfigureAwait(false);
            }
            catch (RequestFailedException exception) when (exception.Status == 404)
            {
                await _indexClient
                    .CreateIndexAsync(
                        ChunkIndexSchema.Build(_indexName, _searchOptions, _openAIOptions),
                        cancellationToken)
                    .ConfigureAwait(false);

                LogIndexCreated(_indexName, _openAIOptions.EmbeddingDimensions);
            }

            _indexVerified = true;
            return Result.Success();
        }
        catch (RequestFailedException exception) when (exception.Status == 409)
        {
            // Another instance created it between our 404 and our create. That is
            // the outcome we wanted.
            LogIndexCreatedConcurrently(_indexName);
            _indexVerified = true;
            return Result.Success();
        }
        catch (RequestFailedException exception)
        {
            LogIndexUnavailable(exception, _indexName, exception.Status, exception.ErrorCode);
            return Result.Failure(VectorIndexErrors.IndexUnavailable);
        }
        catch (AuthenticationFailedException exception)
        {
            LogAuthenticationFailed(exception);
            return Result.Failure(VectorIndexErrors.AuthenticationFailed);
        }
        catch (AggregateException exception)
        {
            LogTransportFailed(exception, _indexName);
            return Result.Failure(VectorIndexErrors.IndexUnavailable);
        }
        finally
        {
            _indexInitializationLock.Release();
        }
    }

    /// <summary>Releases the initialisation lock.</summary>
    public void Dispose() => _indexInitializationLock.Dispose();

    [LoggerMessage(
        EventId = 6000,
        Level = LogLevel.Information,
        Message = "Indexed {ChunkCount} chunks for document {DocumentId} into {IndexName}.")]
    private partial void LogChunksIndexed(Guid documentId, int chunkCount, string indexName);

    [LoggerMessage(
        EventId = 6001,
        Level = LogLevel.Error,
        Message = "Failed to index chunks {Offset}..{Count} of document {DocumentId}. Search returned {Status} ({ErrorCode}).")]
    private partial void LogIndexingFailed(Exception exception, Guid documentId, int offset, int count, int status, string? errorCode);

    [LoggerMessage(
        EventId = 6002,
        Level = LogLevel.Error,
        Message = "Search accepted the batch but rejected chunks: {Succeeded} of {Expected} indexed. " +
                  "First failure was key {Key} with status {Status}: {ErrorMessage}")]
    private partial void LogChunksRejected(int expected, int succeeded, string key, int status, string errorMessage);

    [LoggerMessage(
        EventId = 6003,
        Level = LogLevel.Error,
        Message = "Index {IndexName} is unavailable. Search returned {Status} ({ErrorCode}).")]
    private partial void LogIndexUnavailable(Exception exception, string indexName, int status, string? errorCode);

    [LoggerMessage(
        EventId = 6004,
        Level = LogLevel.Information,
        Message = "Created vector index {IndexName} with {Dimensions}-dimensional vectors.")]
    private partial void LogIndexCreated(string indexName, int dimensions);

    [LoggerMessage(
        EventId = 6005,
        Level = LogLevel.Information,
        Message = "Vector index {IndexName} was created concurrently by another instance.")]
    private partial void LogIndexCreatedConcurrently(string indexName);

    [LoggerMessage(
        EventId = 6006,
        Level = LogLevel.Error,
        Message = "Failed to authenticate to Azure AI Search. Verify the managed identity and its RBAC role assignments.")]
    private partial void LogAuthenticationFailed(Exception exception);

    [LoggerMessage(
        EventId = 6007,
        Level = LogLevel.Error,
        Message = "Azure AI Search was unreachable for {Target} after all retries were exhausted.")]
    private partial void LogTransportFailed(Exception exception, string target);

    [LoggerMessage(
        EventId = 6008,
        Level = LogLevel.Error,
        Message = "Vector dimension mismatch for chunk {ChunkId}: the index expects {Expected} but the vector has {Actual}.")]
    private partial void LogDimensionMismatch(Guid chunkId, int expected, int actual);

    [LoggerMessage(
        EventId = 6009,
        Level = LogLevel.Error,
        Message = "Document {DocumentId} was partially indexed: {Indexed} of {Total} chunks were written before the failure. " +
                  "Re-indexing the document will converge, because chunk writes are idempotent.")]
    private partial void LogPartialIndexing(Guid documentId, int indexed, int total);
}
