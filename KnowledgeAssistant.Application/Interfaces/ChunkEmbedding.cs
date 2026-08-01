namespace KnowledgeAssistant.Application.Interfaces;

/// <summary>
/// The vector representation of one document chunk.
/// </summary>
/// <param name="ChunkId">
/// The chunk this vector was generated from, matching
/// <see cref="DocumentChunk.ChunkId"/>.
/// </param>
/// <param name="Vector">The embedding vector.</param>
/// <remarks>
/// <para>
/// <b>Why the id travels with the vector.</b> A bare list of vectors would be
/// correct only as long as nothing reordered it, and the consequence of a
/// reorder is invisible: every chunk keeps a vector, just the wrong one, and the
/// only symptom is search results that are subtly and inexplicably poor. Pairing
/// each vector with its chunk id makes that class of bug impossible to express.
/// </para>
/// <para>
/// <b>Why <see cref="ReadOnlyMemory{T}"/> rather than <c>float[]</c>.</b> It is a
/// BCL type, so no vendor type crosses the boundary, and it is what the
/// underlying client already hands back — taking it directly avoids copying a
/// 1536-element array once per chunk for no benefit. Being read-only also states
/// that the vector is not the caller's to mutate.
/// </para>
/// </remarks>
public sealed record ChunkEmbedding(
    Guid ChunkId,
    ReadOnlyMemory<float> Vector);
