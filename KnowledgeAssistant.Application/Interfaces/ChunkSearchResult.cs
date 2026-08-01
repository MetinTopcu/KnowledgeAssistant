namespace KnowledgeAssistant.Application.Interfaces;

/// <summary>
/// One chunk returned by a similarity search, with the score that selected it.
/// </summary>
/// <param name="ChunkId">The chunk's identifier in the retrieval index.</param>
/// <param name="DocumentId">The document the chunk came from.</param>
/// <param name="ChunkOrder">The chunk's position in that document.</param>
/// <param name="Text">The chunk's text.</param>
/// <param name="BlobUri">The absolute location of the source document.</param>
/// <param name="Score">
/// The relevance score the search service assigned. Higher is closer.
/// </param>
/// <remarks>
/// <para>
/// Primitives, <see cref="Uri"/>, and a <see cref="double"/> — no vendor type.
/// Returning the SDK's <c>SearchResult&lt;T&gt;</c> would put
/// <c>Azure.Search.Documents</c> into the Application layer's public surface,
/// which is exactly what <c>Interfaces/README.md</c> warns against: an interface
/// that returns the SDK's own result type is the vendor wearing a disguise.
/// </para>
/// <para>
/// <b>The score is carried but not interpreted.</b> Its scale depends on the
/// distance metric and the algorithm, so it is meaningful for ranking and for
/// showing a user why a source was chosen — and meaningless as an absolute
/// threshold. Nothing in this system filters on it.
/// </para>
/// </remarks>
public sealed record ChunkSearchResult(
    Guid ChunkId,
    Guid DocumentId,
    int ChunkOrder,
    string Text,
    Uri BlobUri,
    double Score);
