namespace KnowledgeAssistant.Application.Queries.Documents.List;

/// <summary>
/// One document in the corpus, as the client sees it.
/// </summary>
/// <param name="DocumentId">The identifier assigned at upload.</param>
/// <param name="FileName">The file name supplied at upload, already sanitised.</param>
/// <param name="BlobName">The storage-relative name the content was written under.</param>
/// <param name="UploadedAtUtc">When the upload was accepted, in UTC.</param>
/// <remarks>
/// <para>
/// <b><c>BlobUri</c> is deliberately absent</b>, exactly as it is from the upload
/// response: it names internal storage topology, no client can use it without a
/// credential it does not have, and publishing an account host in every list
/// response tells a reader more about the deployment than about the document.
/// <paramref name="BlobName"/> is kept because it is the one handle an operator
/// needs to find the object in storage.
/// </para>
/// <para>
/// <b>There is no size, content type, or chunk count here</b> because the index
/// does not hold them. See <c>DocumentIndexEntry</c>: returning zeros would be worse
/// than returning nothing, since a zero is indistinguishable from a measurement.
/// </para>
/// </remarks>
public sealed record DocumentSummary(
    Guid DocumentId,
    string FileName,
    string BlobName,
    DateTimeOffset UploadedAtUtc);

/// <summary>
/// The corpus listing.
/// </summary>
/// <param name="Documents">The documents, newest upload first. Possibly empty.</param>
/// <param name="Count">How many documents are in <paramref name="Documents"/>.</param>
/// <param name="Truncated">
/// Whether the listing filled the requested limit, meaning there may be more.
/// </param>
/// <remarks>
/// <para>
/// An object rather than a bare array. A JSON array is a shape that cannot grow:
/// the day this needs a total, a cursor, or a warning, every client has to change
/// at once. It also lets <paramref name="Truncated"/> exist, which a list alone
/// cannot express.
/// </para>
/// <para>
/// <b><paramref name="Truncated"/> is a "maybe", not a "yes".</b> A corpus of
/// exactly <c>MaxResults</c> documents reports <see langword="true"/> while
/// having nothing more to show. Knowing for certain would cost a second call for
/// a count, every time, to sharpen a hint — and a client that wants certainty can
/// ask for one more than it intends to display.
/// </para>
/// </remarks>
public sealed record ListDocumentsResponse(
    IReadOnlyList<DocumentSummary> Documents,
    int Count,
    bool Truncated);
