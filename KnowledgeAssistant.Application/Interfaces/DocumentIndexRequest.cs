namespace KnowledgeAssistant.Application.Interfaces;

/// <summary>
/// The document metadata to be made searchable.
/// </summary>
/// <param name="DocumentId">The identifier assigned at upload; the index key.</param>
/// <param name="BlobName">The storage-relative name the content was written under.</param>
/// <param name="OriginalFileName">The file name supplied by the client, already sanitised.</param>
/// <param name="BlobUri">The absolute location of the stored blob.</param>
/// <param name="UploadedAt">When the upload was accepted, in UTC.</param>
/// <remarks>
/// <para>
/// Primitives, <see cref="Uri"/>, and <see cref="DateTimeOffset"/> only — the
/// same rule <c>BlobUploadResult</c> follows. Taking the adapter's indexed model
/// here instead would put <c>Azure.Search.Documents</c> attributes into the
/// Application layer's public surface and name a vendor in the port's signature,
/// which is the coupling the port exists to prevent.
/// </para>
/// <para>
/// <b>Metadata only, by design.</b> There is no content, no chunk, and no vector
/// here. This slice makes documents *findable by their properties*; making them
/// findable by their meaning is a later, separate decision with its own index
/// schema implications.
/// </para>
/// </remarks>
public sealed record DocumentIndexRequest(
    Guid DocumentId,
    string BlobName,
    string OriginalFileName,
    Uri BlobUri,
    DateTimeOffset UploadedAt);
