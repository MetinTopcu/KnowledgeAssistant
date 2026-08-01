namespace KnowledgeAssistant.Application.Interfaces;

/// <summary>
/// One chunk together with the vector generated from it, ready to be indexed.
/// </summary>
/// <param name="ChunkId">The chunk's stable identifier; the index key.</param>
/// <param name="ChunkOrder">The chunk's zero-based position in its document.</param>
/// <param name="Text">The chunk's text.</param>
/// <param name="Vector">The embedding generated from <paramref name="Text"/>.</param>
/// <remarks>
/// <para>
/// <b>Why the text and the vector travel together in one type.</b> The caller
/// holds a list of <see cref="DocumentChunk"/> and a list of
/// <see cref="ChunkEmbedding"/>, and the obvious port would accept both. That
/// port would then have to trust — or re-verify — that the two lists correspond,
/// and the failure mode when they do not is silent: every chunk is indexed with
/// another chunk's vector, retrieval returns confidently wrong passages, and
/// nothing in the system reports an error. Requiring them paired up front makes
/// that state unrepresentable rather than merely detectable.
/// </para>
/// <para>
/// The join itself is trivial — both are keyed by chunk id — and belongs to
/// whichever use case holds both halves, which is the only place that knows they
/// describe the same document.
/// </para>
/// </remarks>
public sealed record VectorIndexChunk(
    Guid ChunkId,
    int ChunkOrder,
    string Text,
    ReadOnlyMemory<float> Vector);
