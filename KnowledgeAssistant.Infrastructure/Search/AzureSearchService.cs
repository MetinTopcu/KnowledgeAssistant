using Azure;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using KnowledgeAssistant.Application.Interfaces;
using KnowledgeAssistant.Domain.Common;
using KnowledgeAssistant.Infrastructure.Search.Vectors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAssistant.Infrastructure.Search;

/// <summary>
/// Indexes document metadata in Azure AI Search.
/// </summary>
/// <remarks>
/// <para>
/// <b>Registered as a singleton</b>, for the same reasons as the blob adapter:
/// <see cref="SearchIndexClient"/> and <see cref="SearchClient"/> are thread-safe
/// and hold a pooled <c>HttpClient</c> plus a cached access token. Constructing
/// one per request would re-run the credential chain and exhaust sockets under
/// load, and would make the index-existence cache below pointless.
/// </para>
/// <para>
/// <b>Failures become values.</b> Every Azure exception is logged in full — stack
/// trace, status, error code — and then converted to a <see cref="SearchErrors"/>
/// value. Converting without logging first would discard exactly the detail an
/// outage needs to be diagnosed from.
/// </para>
/// </remarks>
internal sealed partial class AzureSearchService : IAzureSearchService, IDisposable
{
    private readonly SearchIndexClient _indexClient;
    private readonly SearchClient _searchClient;
    private readonly SearchClient _chunkSearchClient;
    private readonly string _indexName;
    private readonly string _chunkIndexName;
    private readonly ILogger<AzureSearchService> _logger;

    // Guards the one-time index check. Deliberately not a Lazy<Task>: that caches
    // a faulted task permanently, so one transient failure at startup would
    // poison every later upload for the process lifetime. This pair re-attempts
    // until it succeeds once.
    private readonly SemaphoreSlim _indexInitializationLock = new(1, 1);
    private volatile bool _indexVerified;

    /// <summary>Initialises the adapter.</summary>
    public AzureSearchService(
        SearchIndexClient indexClient,
        IOptions<AzureSearchOptions> options,
        ILogger<AzureSearchService> logger)
    {
        ArgumentNullException.ThrowIfNull(indexClient);
        ArgumentNullException.ThrowIfNull(options);

        _indexClient = indexClient;
        _indexName = options.Value.IndexName;

        _chunkIndexName = options.Value.ChunkIndexName;

        // Derived from the index client rather than registered separately, so both
        // clients provably share one pipeline, one credential, and one token cache.
        _searchClient = indexClient.GetSearchClient(_indexName);

        // A second client, for the chunk index this service reads but does not
        // own. Creating that index is the vector adapter's job; querying it needs
        // nothing but a client, and duplicating the provisioning logic here to get
        // one would be the actual mistake.
        _chunkSearchClient = indexClient.GetSearchClient(_chunkIndexName);

        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result> IndexDocumentAsync(
        DocumentIndexRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result indexResult = await EnsureIndexExistsAsync(cancellationToken).ConfigureAwait(false);

        if (indexResult.IsFailure)
        {
            return indexResult;
        }

        var document = new SearchDocument
        {
            DocumentId = request.DocumentId.ToString(),
            BlobName = request.BlobName,
            OriginalFileName = request.OriginalFileName,
            BlobUri = request.BlobUri.AbsoluteUri,
            UploadedAt = request.UploadedAt,
        };

        Response<IndexDocumentsResult> response;

        try
        {
            // MergeOrUpload rather than Upload: the document id is the index key,
            // so a retry updates in place instead of duplicating, and a later
            // enrichment step writing additional fields to the same document will
            // not have them wiped by a re-index of this metadata.
            //
            // ThrowOnAnyError is left at its default of false on purpose — see the
            // per-document check below.
            response = await _searchClient
                .MergeOrUploadDocumentsAsync([document], options: null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (RequestFailedException exception)
        {
            LogIndexingFailed(exception, request.DocumentId, exception.Status, exception.ErrorCode);
            return Result.Failure(SearchErrors.IndexingFailed);
        }
        catch (AuthenticationFailedException exception)
        {
            LogAuthenticationFailed(exception);
            return Result.Failure(SearchErrors.AuthenticationFailed);
        }
        catch (AggregateException exception)
        {
            // Azure's retry policy throws this once every attempt has failed at the
            // transport level (DNS, TLS, socket) — not RequestFailedException.
            // Catching only the latter lets a total outage escape unhandled.
            LogTransportFailed(exception, _indexName);
            return Result.Failure(SearchErrors.IndexingFailed);
        }

        // The critical check. Azure AI Search reports per-document outcomes inside
        // a 200 response: a document can be rejected — throttled, malformed key,
        // schema mismatch — while the HTTP call itself succeeds. Treating "no
        // exception" as "indexed" is the standard way to end up with an index that
        // is quietly missing documents nobody noticed were dropped.
        IReadOnlyList<IndexingResult> results = response.Value.Results;
        IndexingResult? documentResult = results.Count > 0 ? results[0] : null;

        if (documentResult is null || !documentResult.Succeeded)
        {
            LogDocumentRejected(
                request.DocumentId,
                documentResult?.Status ?? 0,
                documentResult?.ErrorMessage ?? "The service returned no result for the document.");

            return Result.Failure(SearchErrors.DocumentRejected);
        }

        LogIndexingSucceeded(request.DocumentId, _indexName);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ChunkSearchResult>>> SearchChunksAsync(
        ReadOnlyMemory<float> queryVector,
        int topK,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);

        if (queryVector.IsEmpty)
        {
            throw new ArgumentException("The query vector must not be empty.", nameof(queryVector));
        }

        // Field names come from the indexed model rather than string literals, so
        // a rename that would silently break retrieval fails to compile instead.
        var vectorQuery = new VectorizedQuery(queryVector)
        {
            KNearestNeighborsCount = topK,
            Fields = { nameof(ChunkSearchDocument.Embedding) },
        };

        var searchOptions = new SearchOptions
        {
            // Bounds the response as well as the vector search. Without it the
            // service applies its own default page size, which need not match the
            // number of neighbours requested.
            Size = topK,
            VectorSearch = new VectorSearchOptions { Queries = { vectorQuery } },

            // Selecting explicitly keeps the response to what a citation needs.
            // The vector field is excluded anyway — the schema marks it hidden —
            // but naming the fields means adding one to the index does not quietly
            // start inflating every search response.
            Select =
            {
                nameof(ChunkSearchDocument.ChunkId),
                nameof(ChunkSearchDocument.DocumentId),
                nameof(ChunkSearchDocument.ChunkOrder),
                nameof(ChunkSearchDocument.ChunkText),
                nameof(ChunkSearchDocument.BlobUri),
            },
        };

        try
        {
            // A null search text makes this a pure vector query: no keyword
            // matching, and therefore no accidental hybrid retrieval.
            Response<SearchResults<ChunkSearchDocument>> response = await _chunkSearchClient
                .SearchAsync<ChunkSearchDocument>(searchText: null, searchOptions, cancellationToken)
                .ConfigureAwait(false);

            var results = new List<ChunkSearchResult>(topK);

            await foreach (SearchResult<ChunkSearchDocument> hit in
                response.Value.GetResultsAsync().WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                ChunkSearchResult? mapped = MapHit(hit);

                if (mapped is not null)
                {
                    results.Add(mapped);
                }
            }

            LogChunksRetrieved(results.Count, topK, _chunkIndexName);

            return results;
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            // The chunk index has never been created, which means nothing has been
            // ingested yet. Distinguished from a general failure because the remedy
            // is to ingest a document, not to investigate an outage.
            LogChunkIndexMissing(exception, _chunkIndexName);
            return Result.Failure<IReadOnlyList<ChunkSearchResult>>(SearchErrors.ChunkIndexUnavailable);
        }
        catch (RequestFailedException exception)
        {
            LogSearchFailed(exception, _chunkIndexName, exception.Status, exception.ErrorCode);
            return Result.Failure<IReadOnlyList<ChunkSearchResult>>(SearchErrors.SearchFailed);
        }
        catch (AuthenticationFailedException exception)
        {
            LogAuthenticationFailed(exception);
            return Result.Failure<IReadOnlyList<ChunkSearchResult>>(SearchErrors.AuthenticationFailed);
        }
        catch (AggregateException exception)
        {
            LogTransportFailed(exception, _chunkIndexName);
            return Result.Failure<IReadOnlyList<ChunkSearchResult>>(SearchErrors.SearchFailed);
        }
    }

    /// <summary>
    /// Maps one hit onto the port's vendor-neutral result.
    /// </summary>
    /// <remarks>
    /// Returns <see langword="null"/> for a document whose identifiers or source
    /// URI cannot be parsed. Such a record cannot be cited — nothing could resolve
    /// it back to a document — so it is dropped and logged rather than surfaced as
    /// a citation pointing nowhere. It should be unreachable: this index is only
    /// ever written by the vector adapter.
    /// </remarks>
    private ChunkSearchResult? MapHit(SearchResult<ChunkSearchDocument> hit)
    {
        ChunkSearchDocument document = hit.Document;

        if (!Guid.TryParse(document.ChunkId, out Guid chunkId) ||
            !Guid.TryParse(document.DocumentId, out Guid documentId) ||
            !Uri.TryCreate(document.BlobUri, UriKind.Absolute, out Uri? blobUri))
        {
            LogUnmappableHit(document.ChunkId ?? "(null)");
            return null;
        }

        return new ChunkSearchResult(
            ChunkId: chunkId,
            DocumentId: documentId,
            ChunkOrder: document.ChunkOrder,
            Text: document.ChunkText,
            BlobUri: blobUri,
            Score: hit.Score ?? 0);
    }

    /// <summary>
    /// Creates the index on first use, then remembers that it exists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Checking on every call would cost a round trip per upload for a condition
    /// that changes at most once in the lifetime of a deployment. The
    /// double-checked flag keeps the steady-state cost at a single volatile read.
    /// </para>
    /// <para>
    /// <b>Create, never CreateOrUpdate.</b> <c>CreateOrUpdateIndexAsync</c> would
    /// silently reshape a live index on the next deployment — and some schema
    /// changes in Azure AI Search require a full rebuild and reload, so an
    /// automatic update can quietly leave an index that no longer matches the
    /// documents in it. Changing an existing schema is a migration someone should
    /// perform deliberately, not a side effect of an upload.
    /// </para>
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
                    .CreateIndexAsync(DocumentIndexSchema.Build(_indexName), cancellationToken)
                    .ConfigureAwait(false);

                LogIndexCreated(_indexName);
            }

            _indexVerified = true;
            return Result.Success();
        }
        catch (RequestFailedException exception) when (exception.Status == 409)
        {
            // Another instance created the index between our 404 and our create.
            // That is the outcome we wanted, so it is a success, not a failure.
            LogIndexCreatedConcurrently(_indexName);
            _indexVerified = true;
            return Result.Success();
        }
        catch (RequestFailedException exception)
        {
            LogIndexUnavailable(exception, _indexName, exception.Status, exception.ErrorCode);
            return Result.Failure(SearchErrors.IndexUnavailable);
        }
        catch (AuthenticationFailedException exception)
        {
            LogAuthenticationFailed(exception);
            return Result.Failure(SearchErrors.AuthenticationFailed);
        }
        catch (AggregateException exception)
        {
            LogTransportFailed(exception, _indexName);
            return Result.Failure(SearchErrors.IndexUnavailable);
        }
        finally
        {
            _indexInitializationLock.Release();
        }
    }

    /// <summary>Releases the initialisation lock.</summary>
    public void Dispose() => _indexInitializationLock.Dispose();

    // Source-generated logging: the generator emits a cached delegate per message,
    // so a disabled level costs a branch rather than boxing every argument.

    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Information,
        Message = "Indexed document {DocumentId} into {IndexName}.")]
    private partial void LogIndexingSucceeded(Guid documentId, string indexName);

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Error,
        Message = "Failed to index document {DocumentId}. Search returned {Status} ({ErrorCode}).")]
    private partial void LogIndexingFailed(Exception exception, Guid documentId, int status, string? errorCode);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Error,
        Message = "Search accepted the request but rejected document {DocumentId} with status {Status}: {ErrorMessage}")]
    private partial void LogDocumentRejected(Guid documentId, int status, string errorMessage);

    [LoggerMessage(
        EventId = 2003,
        Level = LogLevel.Error,
        Message = "Index {IndexName} is unavailable. Search returned {Status} ({ErrorCode}).")]
    private partial void LogIndexUnavailable(Exception exception, string indexName, int status, string? errorCode);

    [LoggerMessage(
        EventId = 2004,
        Level = LogLevel.Information,
        Message = "Created search index {IndexName}.")]
    private partial void LogIndexCreated(string indexName);

    [LoggerMessage(
        EventId = 2005,
        Level = LogLevel.Information,
        Message = "Search index {IndexName} was created concurrently by another instance.")]
    private partial void LogIndexCreatedConcurrently(string indexName);

    [LoggerMessage(
        EventId = 2006,
        Level = LogLevel.Error,
        Message = "Failed to authenticate to Azure AI Search. Verify the managed identity and its RBAC role assignments.")]
    private partial void LogAuthenticationFailed(Exception exception);

    [LoggerMessage(
        EventId = 2007,
        Level = LogLevel.Error,
        Message = "Azure AI Search was unreachable for {Target} after all retries were exhausted.")]
    private partial void LogTransportFailed(Exception exception, string target);

    [LoggerMessage(
        EventId = 2008,
        Level = LogLevel.Debug,
        Message = "Vector search returned {ResultCount} of {TopK} requested chunks from {IndexName}.")]
    private partial void LogChunksRetrieved(int resultCount, int topK, string indexName);

    [LoggerMessage(
        EventId = 2009,
        Level = LogLevel.Warning,
        Message = "Chunk index {IndexName} does not exist. No documents have been ingested yet.")]
    private partial void LogChunkIndexMissing(Exception exception, string indexName);

    [LoggerMessage(
        EventId = 2010,
        Level = LogLevel.Error,
        Message = "Vector search against {IndexName} failed. Search returned {Status} ({ErrorCode}).")]
    private partial void LogSearchFailed(Exception exception, string indexName, int status, string? errorCode);

    [LoggerMessage(
        EventId = 2011,
        Level = LogLevel.Error,
        Message = "Dropped a search hit whose identifiers could not be parsed: chunk key {ChunkKey}.")]
    private partial void LogUnmappableHit(string chunkKey);
}
