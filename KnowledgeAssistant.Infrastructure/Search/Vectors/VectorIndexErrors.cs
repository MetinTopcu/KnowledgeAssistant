using KnowledgeAssistant.Domain.Common;

namespace KnowledgeAssistant.Infrastructure.Search.Vectors;

/// <summary>
/// The failures the vector indexing adapter can report.
/// </summary>
/// <remarks>
/// Generic descriptions, and all <see cref="ErrorType.Failure"/>, for the same
/// reasons as the other Search errors: the chunks and vectors were produced by
/// this system, so a rejection is a problem with the index, the schema, or the
/// service — never with anything the caller could change.
/// </remarks>
internal static class VectorIndexErrors
{
    /// <summary>The index could not be created or confirmed to exist.</summary>
    internal static readonly Error IndexUnavailable = Error.Failure(
        "VectorIndex.IndexUnavailable",
        "The retrieval index is currently unavailable.");

    /// <summary>The service rejected the indexing request outright.</summary>
    internal static readonly Error IndexingFailed = Error.Failure(
        "VectorIndex.IndexingFailed",
        "The document chunks could not be indexed.");

    /// <summary>
    /// The request was accepted but individual chunks were not indexed.
    /// </summary>
    /// <remarks>
    /// The trap this adapter exists to avoid, and the reason per-document results
    /// are inspected rather than trusted: Azure AI Search answers 200 for a batch
    /// in which documents failed, so treating the absence of an exception as
    /// success leaves an index quietly missing chunks that nothing will ever
    /// report.
    /// </remarks>
    internal static readonly Error ChunksRejected = Error.Failure(
        "VectorIndex.ChunksRejected",
        "Some document chunks could not be indexed.");

    /// <summary>A vector did not match the index's configured dimensions.</summary>
    /// <remarks>
    /// Caught before the request is sent. The service would reject it anyway, but
    /// only after the whole batch had been serialised and uploaded — and its error
    /// names a field, not the mismatch. Checking locally turns a confusing 400
    /// into a message that states both numbers.
    /// </remarks>
    internal static readonly Error DimensionMismatch = Error.Failure(
        "VectorIndex.DimensionMismatch",
        "A vector did not match the size the retrieval index expects.");

    /// <summary>The application could not authenticate to Azure AI Search.</summary>
    internal static readonly Error AuthenticationFailed = Error.Failure(
        "VectorIndex.AuthenticationFailed",
        "The retrieval index is currently unavailable.");
}
