using KnowledgeAssistant.Domain.Common;

namespace KnowledgeAssistant.Infrastructure.Search.Chunking;

/// <summary>
/// Extracts a document's plain text from its bytes.
/// </summary>
/// <remarks>
/// <para>
/// <b>An Infrastructure-internal seam, not an Application port.</b> It is
/// <c>internal</c> on purpose. Application asked for chunks, not for a choice of
/// extraction engine — promoting this to <c>Application/Interfaces</c> would
/// publish an implementation detail to a layer that has no basis for choosing
/// between the implementations and no way to benefit from knowing.
/// </para>
/// <para>
/// What it buys inside Infrastructure is the thing that makes the chunking
/// service testable: <c>DocumentChunkingService</c> depends on this rather than
/// on a PDF parser, so its behaviour can be exercised with a stub that returns a
/// known string, without a single PDF fixture on disk.
/// </para>
/// </remarks>
internal interface IPdfTextExtractor
{
    /// <summary>Extracts the text content of <paramref name="content"/>.</summary>
    /// <param name="content">
    /// The document's bytes, positioned at the start. Read but not disposed.
    /// </param>
    /// <param name="cancellationToken">Cancelled when the caller disconnects.</param>
    /// <returns>
    /// The extracted text — possibly empty if the document has no text layer —
    /// or the failure that prevented extraction.
    /// </returns>
    Task<Result<string>> ExtractTextAsync(Stream content, CancellationToken cancellationToken);
}
