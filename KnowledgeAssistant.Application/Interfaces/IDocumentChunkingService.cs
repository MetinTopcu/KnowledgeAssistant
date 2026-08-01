using KnowledgeAssistant.Domain.Common;

namespace KnowledgeAssistant.Application.Interfaces;

/// <summary>
/// Turns a document's bytes into ordered, overlapping text chunks.
/// </summary>
/// <remarks>
/// <para>
/// An outbound port, like <see cref="IBlobStorageService"/> and
/// <see cref="IAzureSearchService"/>: Application declares what it needs and
/// Infrastructure conforms, so the dependency arrow points inward against the
/// flow of control.
/// </para>
/// <para>
/// <b>Extraction is deliberately hidden behind this port.</b> The caller does not
/// choose between Azure Document Intelligence and local parsing, and cannot tell
/// which ran. That choice depends on deployment configuration, not on the use
/// case, so exposing it here would push an infrastructure concern into
/// Application and give every caller a decision it has no basis to make.
/// </para>
/// <para>
/// <b>This is not a repository.</b> Nothing is stored or retrieved — this is a
/// pure transformation of bytes into values, in the same family as a parser.
/// </para>
/// </remarks>
public interface IDocumentChunkingService
{
    /// <summary>
    /// Extracts the document's text and splits it into chunks.
    /// </summary>
    /// <param name="documentId">
    /// The identifier the chunk ids are derived from, so that a chunk can be
    /// traced back to its document without a lookup table.
    /// </param>
    /// <param name="content">
    /// The document's bytes. Read but not disposed by this method; the caller
    /// retains ownership.
    /// </param>
    /// <param name="cancellationToken">Cancelled when the caller disconnects.</param>
    /// <returns>
    /// The chunks in reading order, or the failure that prevented chunking. Never
    /// an empty collection: a document from which no text could be read is
    /// reported as a failure rather than as zero chunks, because silently
    /// producing nothing is indistinguishable from success and would let an
    /// unreadable document pass through the pipeline unnoticed.
    /// </returns>
    /// <remarks>
    /// <b>The stream must be positioned at the start.</b> A caller that has
    /// already read these bytes for another purpose has to rewind a seekable
    /// stream, or obtain a fresh one, before calling this.
    /// </remarks>
    Task<Result<IReadOnlyList<DocumentChunk>>> ChunkAsync(
        Guid documentId,
        Stream content,
        CancellationToken cancellationToken);
}
