using KnowledgeAssistant.Domain.Common;

namespace KnowledgeAssistant.Infrastructure.Azure.OpenAI;

/// <summary>
/// The failures the embedding adapter can report.
/// </summary>
/// <remarks>
/// <para>
/// Descriptions are generic for the same reason as the other adapters': Azure's
/// messages carry resource names, deployment names, and request ids that belong
/// in a log rather than in an HTTP response. The adapter logs the exception in
/// full and returns one of these.
/// </para>
/// <para>
/// All are <see cref="ErrorType.Failure"/>. Nothing here is the caller's fault:
/// the chunks were produced by this system, so a rejection is a problem with the
/// deployment, the quota, or the configuration.
/// </para>
/// </remarks>
internal static class EmbeddingErrors
{
    /// <summary>The embedding request failed and retries did not recover it.</summary>
    internal static readonly Error GenerationFailed = Error.Failure(
        "Embedding.GenerationFailed",
        "Embeddings could not be generated.");

    /// <summary>The deployment's rate limit was still being exceeded after retries.</summary>
    /// <remarks>
    /// Separated from <see cref="GenerationFailed"/> because it is the one
    /// failure here with an operational rather than a diagnostic response:
    /// nothing is broken, the deployment is simply too small for the load, and
    /// the fix is quota or throughput rather than debugging. Merging it into the
    /// general failure would hide a capacity problem inside a bucket that reads
    /// as defects.
    /// </remarks>
    internal static readonly Error RateLimited = Error.Failure(
        "Embedding.RateLimited",
        "The embedding service is currently rate limited. Try again shortly.");

    /// <summary>The application could not authenticate to Azure OpenAI.</summary>
    internal static readonly Error AuthenticationFailed = Error.Failure(
        "Embedding.AuthenticationFailed",
        "Embedding generation is currently unavailable.");

    /// <summary>
    /// The service returned a different number of vectors than chunks were sent.
    /// </summary>
    /// <remarks>
    /// Should be unreachable. It exists because the only thing tying a returned
    /// vector to its input is position — the SDK exposes no index on a returned
    /// embedding — so a count mismatch is the single detectable symptom of the
    /// alignment having gone wrong. Proceeding on a mismatch would attach vectors
    /// to the wrong chunks, which no later stage could detect or repair.
    /// </remarks>
    internal static readonly Error ResponseMismatch = Error.Failure(
        "Embedding.ResponseMismatch",
        "The embedding service returned an unexpected response.");

    /// <summary>A returned vector did not have the configured length.</summary>
    /// <remarks>
    /// Means the deployment is serving a different model than the configuration
    /// expects. Indexing vectors of mixed length produces an index that cannot be
    /// searched coherently and gives no error while being built.
    /// </remarks>
    internal static readonly Error DimensionMismatch = Error.Failure(
        "Embedding.DimensionMismatch",
        "The embedding service returned vectors of an unexpected size.");
}
