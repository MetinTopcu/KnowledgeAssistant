using KnowledgeAssistant.Domain.Common;

namespace KnowledgeAssistant.Application.Interfaces;

/// <summary>
/// Stores document content in durable object storage.
/// </summary>
/// <remarks>
/// <para>
/// An outbound port, per <c>Interfaces/README.md</c>: the Application layer
/// declares what it needs and Infrastructure conforms. The dependency arrow
/// therefore points inward, against the flow of control, which is what keeps
/// the storage vendor a replaceable detail.
/// </para>
/// <para>
/// <b>This is not a repository.</b> It has no <c>GetById</c>, no <c>Add</c>, no
/// unit of work, and no aggregate to reconstitute — it is a thin adapter over a
/// storage primitive, in the same family as an SMTP or clock port. Blob storage
/// holds opaque bytes, and wrapping it in a repository would imply a collection
/// of domain objects it does not have.
/// </para>
/// <para>
/// It returns <see cref="Result{TValue}"/> rather than throwing so that a
/// storage outage arrives at the caller as a value it must handle, in the same
/// shape as every other failure in the pipeline.
/// </para>
/// </remarks>
public interface IBlobStorageService
{
    /// <summary>
    /// Writes <paramref name="content"/> to storage under a generated,
    /// collision-free name derived from <paramref name="documentId"/>.
    /// </summary>
    /// <param name="documentId">
    /// The identifier the blob name is derived from, so that the identifier
    /// returned by the API and the stored object can be correlated later without
    /// a lookup table.
    /// </param>
    /// <param name="fileName">
    /// The original file name. Used for its extension and recorded as metadata;
    /// it never becomes the blob name.
    /// </param>
    /// <param name="contentType">The content type to store against the blob.</param>
    /// <param name="content">The bytes to write. Read once, not disposed by this method.</param>
    /// <param name="cancellationToken">Cancelled when the caller disconnects.</param>
    /// <returns>The stored blob's name, location, and size, or a failure.</returns>
    Task<Result<BlobUploadResult>> UploadAsync(
        Guid documentId,
        string fileName,
        string contentType,
        Stream content,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads back the content stored under <paramref name="blobName"/>.
    /// </summary>
    /// <param name="blobName">
    /// The name returned by <see cref="UploadAsync"/>, including its virtual
    /// directory prefix.
    /// </param>
    /// <param name="cancellationToken">Cancelled when the caller disconnects.</param>
    /// <returns>
    /// A readable stream positioned at the start, which the caller owns and must
    /// dispose, or the failure that prevented the read.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Why ingestion re-reads bytes it just wrote.</b> The stream the caller
    /// uploaded has been consumed, and rewinding it is only possible because the
    /// web framework happens to buffer request bodies — an implementation detail
    /// of one delivery mechanism that this layer must not depend on. Reading from
    /// storage instead means the pipeline works identically when it is later
    /// driven by a queue message or a reconciliation job, neither of which has an
    /// original stream to rewind.
    /// </para>
    /// <para>
    /// It also makes storage the single source of truth: everything downstream
    /// processes the bytes that were actually persisted, not the bytes that were
    /// supposed to have been.
    /// </para>
    /// <para>
    /// The returned stream is seekable. Parsers that read a file's trailer and
    /// then seek backwards — which PDF requires — would otherwise have to buffer
    /// the whole thing again themselves.
    /// </para>
    /// </remarks>
    Task<Result<Stream>> DownloadAsync(
        string blobName,
        CancellationToken cancellationToken);
}
