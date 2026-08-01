namespace KnowledgeAssistant.Infrastructure.Search.Vectors;

/// <summary>
/// The shape of one chunk record in the retrieval index.
/// </summary>
/// <remarks>
/// <para>
/// <b>No field attributes here, unlike <c>SearchDocument</c>.</b> That model
/// derives its schema through <c>FieldBuilder</c>, which reads attributes — and
/// an attribute argument must be a compile-time constant. The vector field's
/// dimension count comes from configuration, so it cannot be one. The schema for
/// this model is therefore built programmatically in
/// <see cref="ChunkIndexSchema"/>.
/// </para>
/// <para>
/// <b>The two are still tied together.</b> Every field in that schema is named
/// with <c>nameof</c> against a property below, so a rename that would silently
/// desynchronise the index from the payload does not compile instead.
/// </para>
/// <para>
/// Property names are PascalCase for the same reason as the document index: the
/// serializer that writes documents and the schema that describes them must
/// agree on field names, and leaving both at their defaults makes that agreement
/// structural rather than a convention two files have to remember.
/// </para>
/// </remarks>
internal sealed class ChunkSearchDocument
{
    /// <summary>The chunk identifier, and the index key.</summary>
    /// <remarks>
    /// A string because Azure AI Search requires <c>Edm.String</c> keys. The
    /// canonical GUID format is hex digits and dashes, both legal in a key.
    /// </remarks>
    public string ChunkId { get; init; } = string.Empty;

    /// <summary>The document this chunk came from.</summary>
    /// <remarks>
    /// Filterable so that every chunk of one document can be retrieved, counted,
    /// or replaced as a set — the query a re-index or a delete needs, and the
    /// reason this field exists at all in a chunk-keyed index.
    /// </remarks>
    public string DocumentId { get; init; } = string.Empty;

    /// <summary>The chunk's zero-based position in its document.</summary>
    /// <remarks>
    /// Sortable so a passage can be reassembled in reading order, and so the
    /// chunks either side of a hit can be fetched to widen its context.
    /// </remarks>
    public int ChunkOrder { get; init; }

    /// <summary>The chunk's text.</summary>
    /// <remarks>
    /// Searchable, and the only full-text field here. It is also what a retrieval
    /// result must return: a vector match that cannot show the matching text is
    /// an id and a score, which no caller can use.
    /// </remarks>
    public string ChunkText { get; init; } = string.Empty;

    /// <summary>The absolute location of the source document.</summary>
    public string BlobUri { get; init; } = string.Empty;

    /// <summary>When the source document was accepted, in UTC.</summary>
    public DateTimeOffset UploadedAt { get; init; }

    /// <summary>The embedding generated from <see cref="ChunkText"/>.</summary>
    /// <remarks>
    /// <para>
    /// A <see cref="float"/> array rather than the port's
    /// <c>ReadOnlyMemory&lt;float&gt;</c>. The conversion costs one copy per
    /// chunk at the adapter boundary, which buys certainty about how the value
    /// serialises — a vector that silently round-tripped as anything other than a
    /// JSON number array would produce an index that builds without error and
    /// matches nothing.
    /// </para>
    /// </remarks>
    public float[] Embedding { get; init; } = [];
}
