namespace KnowledgeAssistant.Application.Interfaces;

/// <summary>
/// One retrievable fragment of a document's text.
/// </summary>
/// <param name="ChunkId">
/// The chunk's stable identifier. Derived from the document id and
/// <paramref name="ChunkOrder"/>, so re-processing the same document produces
/// the same ids rather than a fresh set.
/// </param>
/// <param name="ChunkOrder">
/// The zero-based position of this chunk in the document, in reading order.
/// </param>
/// <param name="Text">The chunk's text.</param>
/// <remarks>
/// <para>
/// <b>Why the id is stable rather than random.</b> Chunks are destined for an
/// index, and an index is keyed. If ids were regenerated on every run,
/// re-processing a document would write a second complete set of chunks
/// alongside the first — the old ones orphaned, still matching queries, and
/// removable only by knowing what they used to be. A derived id makes the second
/// write an update of the first. It is the same reasoning that made the search
/// adapter use merge-or-upload rather than upload.
/// </para>
/// <para>
/// <b>Why order is carried explicitly</b> rather than left implicit in list
/// position: the moment chunks are persisted, indexed, or returned from a
/// retrieval call, list position is gone. Reassembling a passage in reading
/// order — or fetching the chunks either side of a hit for context — needs this
/// value to have survived the round trip.
/// </para>
/// </remarks>
public sealed record DocumentChunk(
    Guid ChunkId,
    int ChunkOrder,
    string Text);
