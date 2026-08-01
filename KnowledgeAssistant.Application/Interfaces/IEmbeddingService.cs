using KnowledgeAssistant.Domain.Common;

namespace KnowledgeAssistant.Application.Interfaces;

/// <summary>
/// Generates vector representations of document chunks.
/// </summary>
/// <remarks>
/// <para>
/// An outbound port, like every other interface in this folder: Application
/// declares what it needs and Infrastructure conforms. Nothing in the signature
/// names a vendor, a model, a deployment, or a batch — all of which are
/// deployment concerns that would become Application's problem the moment they
/// appeared here.
/// </para>
/// <para>
/// <b>Batching is deliberately invisible.</b> The caller hands over every chunk
/// it has and receives every vector back. How many requests that took is a
/// question of rate limits and token budgets, which change per deployment and
/// per model; exposing a batch size would push a decision outward to a caller
/// with no information to make it.
/// </para>
/// <para>
/// <b>This is not a repository.</b> Nothing is stored or retrieved — this is a
/// transformation, in the same family as the chunking port.
/// </para>
/// </remarks>
public interface IEmbeddingService
{
    /// <summary>
    /// Generates one vector for each supplied chunk.
    /// </summary>
    /// <param name="chunks">The chunks to embed. May be empty.</param>
    /// <param name="cancellationToken">Cancelled when the caller disconnects.</param>
    /// <returns>
    /// One <see cref="ChunkEmbedding"/> per chunk, in the same order, or the
    /// failure that prevented generation.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>All or nothing.</b> A partial result is not returned. Chunks are
    /// embedded so they can be indexed together, and a caller handed nine
    /// vectors for ten chunks has no good option: indexing them leaves a document
    /// silently incomplete, and discarding them wastes the work anyway. Failing
    /// the whole call keeps the decision — retry, or give up — with the caller,
    /// and keeps it explicit.
    /// </para>
    /// <para>
    /// An empty input returns an empty result rather than a failure: asking for
    /// nothing and receiving nothing is a coherent outcome, unlike a document
    /// that yielded no text.
    /// </para>
    /// </remarks>
    Task<Result<IReadOnlyList<ChunkEmbedding>>> GenerateEmbeddingsAsync(
        IReadOnlyList<DocumentChunk> chunks,
        CancellationToken cancellationToken);

    /// <summary>
    /// Generates the vector for a single piece of text.
    /// </summary>
    /// <param name="text">The text to embed. Must not be blank.</param>
    /// <param name="cancellationToken">Cancelled when the caller disconnects.</param>
    /// <returns>The vector, or the failure that prevented generation.</returns>
    /// <remarks>
    /// <para>
    /// <b>Why this exists rather than reusing the batch method.</b> Retrieval
    /// embeds a user's question, and a question is not a document chunk: it has
    /// no identifier, no position in a document, and nothing to index it under.
    /// Satisfying the other method would mean minting a throwaway
    /// <see cref="DocumentChunk"/> whose id and order are lies, at every call
    /// site.
    /// </para>
    /// <para>
    /// It is an overload rather than a second port because the work is identical:
    /// the implementation routes both through the same request path, so retry
    /// policy, dimension validation, and the error taxonomy are shared rather
    /// than reimplemented.
    /// </para>
    /// <para>
    /// <b>The vector must be produced by the same model that indexed the
    /// corpus.</b> Vectors from different models are not comparable, and a
    /// mismatch produces a search that returns confident nonsense rather than an
    /// error. Routing both paths through one service is what makes that
    /// structurally true instead of a thing to remember.
    /// </para>
    /// </remarks>
    Task<Result<ReadOnlyMemory<float>>> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken);
}
