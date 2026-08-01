namespace KnowledgeAssistant.Application.Interfaces;

/// <summary>
/// A document's chunks and vectors, together with the document metadata carried
/// onto each of them.
/// </summary>
/// <param name="DocumentId">The document the chunks came from.</param>
/// <param name="BlobUri">The absolute location of the stored source document.</param>
/// <param name="UploadedAt">When the source document was accepted, in UTC.</param>
/// <param name="Chunks">The chunks and their vectors, in reading order.</param>
/// <remarks>
/// <para>
/// <b>Document metadata appears once here and is denormalised onto every chunk
/// in the index.</b> A search index is not a relational store: there is no join,
/// so a hit that cannot name its document, its source, or its date is a hit
/// nobody can act on. Repeating three small fields per chunk is the cost of
/// every result being self-describing, and it is the standard shape for an index
/// built to be retrieved from.
/// </para>
/// <para>
/// The consequence to be aware of: metadata is copied, so correcting a
/// document's metadata means rewriting all of its chunks. That is the trade
/// being made, and it is the right one while metadata is immutable after upload.
/// </para>
/// </remarks>
public sealed record VectorIndexRequest(
    Guid DocumentId,
    Uri BlobUri,
    DateTimeOffset UploadedAt,
    IReadOnlyList<VectorIndexChunk> Chunks);
