using KnowledgeAssistant.Domain.Common;

namespace KnowledgeAssistant.Infrastructure.Search.Chunking;

/// <summary>
/// The failures the chunking pipeline can report.
/// </summary>
/// <remarks>
/// <para>
/// <b>These are split by whose problem it is.</b> The blob and search adapters
/// return only <see cref="ErrorType.Failure"/>, because a storage outage is never
/// the caller's fault and nothing they change about the request would help. Text
/// extraction is different: an encrypted PDF, a corrupt file, or a scan with no
/// text layer are all properties of the bytes the caller supplied, and telling
/// them so is actionable. Those are <see cref="ErrorType.Validation"/> and
/// surface as 400.
/// </para>
/// <para>
/// Reporting an unreadable upload as 500 would be worse than merely inaccurate:
/// it would put a caller-caused condition into the bucket operators watch for
/// outages, so a stream of malformed uploads would read as a service
/// degradation.
/// </para>
/// </remarks>
internal static class ChunkingErrors
{
    /// <summary>The bytes could not be parsed as a PDF.</summary>
    internal static readonly Error DocumentUnreadable = Error.Validation(
        "Chunking.DocumentUnreadable",
        "The document could not be read. It may be corrupt or not a valid PDF.");

    /// <summary>The PDF is encrypted and cannot be opened without a password.</summary>
    /// <remarks>
    /// Distinct from <see cref="DocumentUnreadable"/> because the remedy is
    /// entirely different and entirely in the caller's hands: supply an
    /// unprotected copy. Collapsing the two would send someone hunting for file
    /// corruption that does not exist.
    /// </remarks>
    internal static readonly Error DocumentPasswordProtected = Error.Validation(
        "Chunking.DocumentPasswordProtected",
        "The document is password protected and cannot be processed.");

    /// <summary>The document parsed, but yielded no text.</summary>
    /// <remarks>
    /// Almost always a scanned image with no text layer. Reported rather than
    /// returned as zero chunks: a document that produces nothing is a failure of
    /// ingestion, and silently succeeding would leave it stored, indexed as
    /// metadata, and permanently unfindable by its content, with no signal that
    /// anything went wrong.
    /// </remarks>
    internal static readonly Error NoTextExtracted = Error.Validation(
        "Chunking.NoTextExtracted",
        "No text could be extracted from the document. It may be a scanned image requiring OCR.");

    /// <summary>The text extraction service could not be reached or failed.</summary>
    /// <remarks>
    /// The one genuine <see cref="ErrorType.Failure"/> here: the caller's document
    /// may be perfectly fine and nothing they do will help.
    /// </remarks>
    internal static readonly Error ExtractionServiceUnavailable = Error.Failure(
        "Chunking.ExtractionServiceUnavailable",
        "Document text extraction is currently unavailable.");
}
