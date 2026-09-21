namespace KnowledgeAssistant.Application.Interfaces;

/// <summary>
/// One document as the index knows it.
/// </summary>
/// <param name="DocumentId">The identifier assigned at upload; the index key.</param>
/// <param name="OriginalFileName">The file name supplied by the client, already sanitised.</param>
/// <param name="BlobName">The storage-relative name the content was written under.</param>
/// <param name="BlobUri">The absolute location of the stored blob.</param>
/// <param name="UploadedAt">When the upload was accepted, in UTC.</param>
/// <remarks>
/// <para>
/// The read counterpart of <see cref="DocumentIndexRequest"/>, and deliberately a
/// separate type rather than a reuse of it. They carry the same fields today, but
/// they answer to different pressures: the write side is fixed by what ingestion
/// knows, the read side by what a caller needs. Sharing one record would mean the
/// first change to either silently changing the other.
/// </para>
/// <para>
/// <b>This is everything the document index holds.</b> There is no content type,
/// size, or chunk count here, because none of them is in the index — they are
/// known at upload and returned by that endpoint, but were never persisted for
/// later reading. Adding them is an index schema change, which in Azure AI Search
/// means recreating the index and re-ingesting the corpus. Reporting the fields
/// that exist is honest; inventing the others would not be.
/// </para>
/// </remarks>
public sealed record DocumentIndexEntry(
    Guid DocumentId,
    string OriginalFileName,
    string BlobName,
    Uri BlobUri,
    DateTimeOffset UploadedAt);
