namespace KnowledgeAssistant.Application.Commands.Documents.Upload;

/// <summary>
/// Confirms that an uploaded file was accepted.
/// </summary>
/// <param name="DocumentId">The identifier assigned to this upload.</param>
/// <param name="FileName">The accepted file name, sanitised of any directory component.</param>
/// <param name="ContentType">The MIME type declared by the client.</param>
/// <param name="SizeInBytes">The number of bytes written to storage.</param>
/// <param name="BlobName">The storage-relative name the document was written under.</param>
/// <param name="ChunkCount">The number of searchable chunks the document produced.</param>
/// <param name="ReceivedAtUtc">When the upload was accepted, in UTC.</param>
/// <remarks>
/// <para>
/// <b>A success here means the document is fully ingested.</b> The bytes are
/// stored, the text is chunked, every chunk is embedded, and both indexes are
/// written. <paramref name="ChunkCount"/> is the visible evidence of that: it is
/// how many passages the document contributed to the retrieval index, and a
/// caller can act on it — a large PDF that yields two chunks extracted badly.
/// </para>
/// <para>
/// There is still no database row, so <paramref name="DocumentId"/> cannot be
/// resolved by a subsequent <c>GET</c>. It remains the caller's correlation
/// handle for logs and support tickets.
/// </para>
/// <para>
/// The endpoint therefore still answers <c>200 OK</c> rather than
/// <c>201 Created</c>: 201 asserts a resource exists at a URL and would oblige a
/// <c>Location</c> header pointing at something no route serves. That changes in
/// the slice that adds persistence and a read endpoint.
/// </para>
/// <para>
/// <b>Why the blob URI is absent.</b> <paramref name="BlobName"/> is returned
/// because it is the handle later operations use, but the absolute URI is
/// withheld. It names the storage account and container, and a client cannot use
/// it — the container is private and access requires an RBAC role no API
/// consumer holds. Publishing internal storage topology to every caller expands
/// the attack surface for no benefit. The full URI is available to the service
/// and recorded in the logs.
/// </para>
/// </remarks>
public sealed record UploadDocumentResponse(
    Guid DocumentId,
    string FileName,
    string ContentType,
    long SizeInBytes,
    string BlobName,
    int ChunkCount,
    DateTimeOffset ReceivedAtUtc);
