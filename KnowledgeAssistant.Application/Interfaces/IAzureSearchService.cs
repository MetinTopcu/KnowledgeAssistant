using KnowledgeAssistant.Domain.Common;

namespace KnowledgeAssistant.Application.Interfaces;

/// <summary>
/// Makes stored documents discoverable by indexing their metadata.
/// </summary>
/// <remarks>
/// <para>
/// An outbound port, like <see cref="IBlobStorageService"/>: Application declares
/// what it needs and Infrastructure conforms, so the dependency arrow points
/// inward against the flow of control.
/// </para>
/// <para>
/// <b>This is not a repository.</b> There is no <c>GetById</c>, no <c>Add</c>, no
/// unit of work, and no aggregate to reconstitute. A search index is a derived,
/// rebuildable projection — a query accelerator, not the state of record — and
/// modelling it as a collection of domain objects would imply a durability
/// guarantee it does not offer.
/// </para>
/// <para>
/// <b>On the name.</b> <c>Interfaces/README.md</c> asks that ports be named in
/// the domain's language rather than the vendor's, which would argue for
/// something like <c>IDocumentIndexService</c>. The name here was specified
/// directly. What actually enforces the boundary is the signature, and that stays
/// vendor-free: no Azure type appears in it, so a second implementation remains
/// possible and a rename is a pure refactor.
/// </para>
/// <para>
/// Returns <see cref="Result"/> rather than throwing, so an indexing outage
/// arrives at the caller as a value it must handle — the same shape as every
/// other failure in the pipeline.
/// </para>
/// </remarks>
public interface IAzureSearchService
{
    /// <summary>
    /// Adds or updates the document's metadata in the index.
    /// </summary>
    /// <param name="request">The metadata to index.</param>
    /// <param name="cancellationToken">Cancelled when the caller disconnects.</param>
    /// <returns>Success, or the failure that prevented indexing.</returns>
    /// <remarks>
    /// Idempotent: indexing the same document twice is an update, not a
    /// duplicate, because <see cref="DocumentIndexRequest.DocumentId"/> is the
    /// index key. That is what makes a retry safe.
    /// </remarks>
    Task<Result> IndexDocumentAsync(
        DocumentIndexRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Finds the chunks whose vectors are closest to <paramref name="queryVector"/>.
    /// </summary>
    /// <param name="queryVector">
    /// The embedding of the query, produced by the same model that embedded the
    /// corpus.
    /// </param>
    /// <param name="topK">The maximum number of chunks to return.</param>
    /// <param name="cancellationToken">Cancelled when the caller disconnects.</param>
    /// <returns>
    /// The matching chunks in descending relevance order — possibly empty — or the
    /// failure that prevented the search.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>An empty result is a success.</b> A corpus that contains nothing
    /// relevant to a question is a fact about the corpus, not a fault, and the
    /// caller has a sensible response to it. Reporting it as a failure would
    /// force every caller to decode an error to discover that nothing went wrong.
    /// </para>
    /// <para>
    /// <b>Pure vector similarity.</b> No keyword matching, no reciprocal-rank
    /// fusion, no semantic reranking — those are distinct retrieval strategies
    /// with their own costs and their own tuning, and adding them silently
    /// underneath this signature would change results without changing the
    /// contract.
    /// </para>
    /// <para>
    /// The query vector is a <see cref="ReadOnlyMemory{T}"/> of
    /// <see cref="float"/> — a BCL type — so no vendor type appears here.
    /// </para>
    /// </remarks>
    Task<Result<IReadOnlyList<ChunkSearchResult>>> SearchChunksAsync(
        ReadOnlyMemory<float> queryVector,
        int topK,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists the indexed documents, newest upload first.
    /// </summary>
    /// <param name="maxResults">The maximum number of documents to return.</param>
    /// <param name="cancellationToken">Cancelled when the caller disconnects.</param>
    /// <returns>
    /// The documents in descending upload order — possibly empty — or the failure
    /// that prevented the listing.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>An empty list is a success, and so is a missing index.</b> The index is
    /// created by the first ingestion, so its absence means nothing has been
    /// uploaded yet. That is a fact about an empty system, not a fault, and a
    /// caller asking "what is in the corpus?" has the same sensible response to
    /// both — which is why the two are not distinguished here.
    /// </para>
    /// <para>
    /// <b>One page, no continuation token.</b> A cursor is a contract about
    /// ordering stability under concurrent writes, and this index has no such
    /// guarantee to offer. <paramref name="maxResults"/> bounds the response
    /// instead; when a corpus grows past what one page can usefully show, paging
    /// is a decision to take deliberately rather than inherit.
    /// </para>
    /// <para>
    /// <b>Newest first</b>, because a corpus browser is almost always asking what
    /// arrived recently. Callers that want another order can sort what they get.
    /// </para>
    /// </remarks>
    Task<Result<IReadOnlyList<DocumentIndexEntry>>> ListDocumentsAsync(
        int maxResults,
        CancellationToken cancellationToken);
}
