using Azure.Search.Documents.Indexes;

namespace KnowledgeAssistant.Infrastructure.Search;

/// <summary>
/// The shape of one document in the search index.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this lives in Infrastructure.</b> The attributes below come from
/// <c>Azure.Search.Documents</c>. Putting this type in Application would drag the
/// Azure SDK across the boundary the ports exist to defend. Application speaks
/// <c>DocumentIndexRequest</c>; this adapter translates.
/// </para>
/// <para>
/// <b>Why a typed model rather than the SDK's <c>SearchDocument</c> dictionary.</b>
/// The dictionary form defers every field name and type to runtime, so a typo
/// becomes a 400 from the service instead of a compile error — and the schema
/// below could not be derived from it. The name collision with
/// <c>Azure.Search.Documents.Models.SearchDocument</c> is harmless: nothing in
/// this project imports that namespace, precisely because this type replaces it.
/// </para>
/// <para>
/// <b>Field names are PascalCase on purpose.</b> <c>FieldBuilder</c> and
/// <c>SearchClient</c> each serialise with their own <c>ObjectSerializer</c>, and
/// if the two disagree the index is created with one set of field names while
/// documents are written with another — the writes then fail, or worse, silently
/// populate nothing. Leaving both at their defaults makes agreement structural
/// rather than something two separate configuration sites must remember.
/// </para>
/// </remarks>
internal sealed class SearchDocument
{
    /// <summary>The document identifier, and the index key.</summary>
    /// <remarks>
    /// A <see cref="string"/> rather than a <see cref="Guid"/> because Azure AI
    /// Search requires the key field to be <c>Edm.String</c>. The canonical "D"
    /// format of a GUID uses only hex digits and dashes, both of which are legal
    /// in a key — a key containing anything else would have to be encoded.
    /// </remarks>
    [SimpleField(IsKey = true, IsFilterable = true)]
    public string DocumentId { get; init; } = string.Empty;

    /// <summary>The storage-relative name the content was written under.</summary>
    /// <remarks>
    /// Filterable so that an operator can locate the index entry for a known
    /// blob, which is the query a reconciliation job needs. Not searchable: it is
    /// a machine-generated path, and full-text analysis of it would return
    /// matches no human asked for.
    /// </remarks>
    [SimpleField(IsFilterable = true)]
    public string BlobName { get; init; } = string.Empty;

    /// <summary>The file name supplied by the client.</summary>
    /// <remarks>
    /// The one full-text field. This is plain keyword indexing — the base
    /// capability of any search index, and explicitly not semantic, vector, or
    /// hybrid retrieval. Without it the index could be filtered but not
    /// searched, which would make it a table with extra steps.
    /// </remarks>
    [SearchableField(IsFilterable = true, IsSortable = true)]
    public string OriginalFileName { get; init; } = string.Empty;

    /// <summary>The absolute location of the stored blob.</summary>
    /// <remarks>
    /// Stored as a string because <see cref="Uri"/> has no
    /// <c>SearchFieldDataType</c> equivalent — <c>FieldBuilder</c> would treat it
    /// as a complex type and emit a nested object nobody wants. Neither
    /// filterable nor searchable: it is a retrieval detail, not a query
    /// dimension.
    /// </remarks>
    [SimpleField]
    public string BlobUri { get; init; } = string.Empty;

    /// <summary>When the upload was accepted, in UTC.</summary>
    /// <remarks>
    /// Filterable, sortable, and facetable: "documents from last week", "newest
    /// first", and a date histogram are the three things a corpus browser always
    /// ends up needing.
    /// </remarks>
    [SimpleField(IsFilterable = true, IsSortable = true, IsFacetable = true)]
    public DateTimeOffset UploadedAt { get; init; }
}
