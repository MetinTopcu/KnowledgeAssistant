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
}
