using KnowledgeAssistant.Domain.Common;

namespace KnowledgeAssistant.Application.Interfaces;

/// <summary>
/// Writes document chunks and their vectors into the retrieval index.
/// </summary>
/// <remarks>
/// <para>
/// An outbound port, like every other interface in this folder. Nothing in the
/// signature names a vendor, an index, an algorithm, or a batch size — all of
/// which are deployment concerns that would become Application's problem the
/// moment they appeared here.
/// </para>
/// <para>
/// <b>Distinct from <see cref="IAzureSearchService"/>, and deliberately so.</b>
/// That port indexes one record per document so a document can be found by its
/// properties. This one indexes one record per chunk so a passage can be found
/// by its content. They differ in granularity, in key, and in what they are for;
/// merging them would mean one of the two lost the key it needs.
/// </para>
/// <para>
/// <b>This is not a repository.</b> There is no read side here at all — no
/// query, no get, no delete. An index is a derived, rebuildable projection, and
/// this port is the write half of building it.
/// </para>
/// </remarks>
public interface IVectorIndexService
{
    /// <summary>
    /// Adds or updates every chunk in <paramref name="request"/> in the index.
    /// </summary>
    /// <param name="request">The document's chunks, vectors, and metadata.</param>
    /// <param name="cancellationToken">Cancelled when the caller disconnects.</param>
    /// <returns>Success, or the failure that prevented indexing.</returns>
    /// <remarks>
    /// <para>
    /// Idempotent: chunk ids are derived from the document and the chunk's
    /// position, so re-indexing the same document updates its chunks in place
    /// rather than duplicating them. That is what makes a retry safe.
    /// </para>
    /// <para>
    /// <b>Not atomic across batches.</b> A document larger than one batch is
    /// written in several requests, and a failure partway through leaves the
    /// earlier batches in the index. This is reported as a failure, and a retry
    /// is the correct response: because the writes are idempotent, repeating the
    /// whole request converges rather than accumulating duplicates.
    /// </para>
    /// <para>
    /// Stale chunks from a <em>previous, longer</em> version of the same document
    /// are not removed — deletion is not part of this port. That matters only
    /// once documents can be replaced, which nothing in the system does yet.
    /// </para>
    /// </remarks>
    Task<Result> IndexChunksAsync(
        VectorIndexRequest request,
        CancellationToken cancellationToken);
}
