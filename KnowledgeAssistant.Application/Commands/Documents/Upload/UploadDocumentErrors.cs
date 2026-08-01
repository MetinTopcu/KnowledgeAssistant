using KnowledgeAssistant.Domain.Common;

namespace KnowledgeAssistant.Application.Commands.Documents.Upload;

/// <summary>
/// Failures that belong to this slice's orchestration rather than to any one
/// service it calls.
/// </summary>
/// <remarks>
/// Deliberately small. Almost every way ingestion can fail is a failure of a
/// service the handler calls, and those already return their own errors — which
/// the handler passes through unaltered rather than re-wrapping. Re-wrapping
/// would flatten five distinct causes into one and cost operators the error code
/// that names the stage. What remains here is the one condition only the
/// orchestrator can observe.
/// </remarks>
internal static class UploadDocumentErrors
{
    /// <summary>A chunk came back from embedding without a vector.</summary>
    /// <remarks>
    /// Unreachable in practice: the embedding port returns one vector per chunk
    /// or fails as a whole, and that contract is enforced on its own side. This
    /// exists because the handler joins two collections, and a join is exactly
    /// where an assumption about correspondence should be checked rather than
    /// trusted. Indexing a chunk with another chunk's vector produces confidently
    /// wrong retrieval that nothing downstream could detect.
    /// </remarks>
    internal static readonly Error EmbeddingMissing = Error.Failure(
        "Ingestion.EmbeddingMissing",
        "The document could not be ingested.");
}
