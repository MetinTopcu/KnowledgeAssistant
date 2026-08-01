using KnowledgeAssistant.Application.Interfaces;

namespace KnowledgeAssistant.Application.Queries.Documents.Ask;

/// <summary>
/// One source the answer was grounded in.
/// </summary>
/// <param name="ReferenceNumber">
/// The number this source was given in the prompt. Markers such as
/// <c>[2]</c> in the answer refer to it.
/// </param>
/// <param name="ChunkId">The chunk's identifier in the retrieval index.</param>
/// <param name="DocumentId">The document the chunk came from.</param>
/// <param name="ChunkOrder">The chunk's position in that document.</param>
/// <param name="Text">The chunk's text, exactly as it was given to the model.</param>
/// <param name="BlobUri">The absolute location of the source document.</param>
/// <param name="Score">The relevance score that selected this chunk.</param>
/// <remarks>
/// <para>
/// <b>Citations are the sources the answer was built from, not the ones the model
/// happened to mention.</b> Every retrieved chunk is returned, numbered exactly
/// as the prompt numbered it. That makes the answer auditable: a reader can check
/// each claim against the text the model actually saw, including the passages it
/// chose to ignore.
/// </para>
/// <para>
/// Deriving a "was this one cited" flag by scanning the answer for <c>[n]</c> was
/// considered and left out. It would be inferred from prose rather than reported
/// by the model, and a flag that is right most of the time is worse than no flag
/// in an audit trail.
/// </para>
/// <para>
/// <paramref name="Text"/> is the full chunk rather than a truncated excerpt: the
/// point of a citation is to be checkable, and an ellipsis is where a
/// misattribution hides.
/// </para>
/// </remarks>
public sealed record AnswerCitation(
    int ReferenceNumber,
    Guid ChunkId,
    Guid DocumentId,
    int ChunkOrder,
    string Text,
    Uri BlobUri,
    double Score);

/// <summary>
/// An answer to a question, with the evidence behind it.
/// </summary>
/// <param name="Answer">The generated answer.</param>
/// <param name="Citations">The sources the answer was grounded in, in rank order.</param>
/// <param name="RetrievedChunkCount">
/// How many chunks retrieval returned. Zero means the corpus held nothing
/// relevant, and the answer says so.
/// </param>
/// <param name="TokenUsage">
/// What the completion cost, or <see langword="null"/> when the provider did not
/// report it or no completion was needed.
/// </param>
/// <param name="AnsweredAtUtc">When the answer was produced, in UTC.</param>
/// <remarks>
/// <para>
/// <b>Every field exists so a caller can judge the answer rather than trust
/// it.</b> The citations show what it was built from,
/// <paramref name="RetrievedChunkCount"/> shows whether it had anything to work
/// with, and <paramref name="TokenUsage"/> shows what it cost. An answer returned
/// on its own is unfalsifiable, and an unfalsifiable answer from a language model
/// is the failure mode this whole pipeline exists to avoid.
/// </para>
/// <para>
/// A zero <paramref name="RetrievedChunkCount"/> is a success, not an error: it
/// carries an honest "nothing in the corpus covers this" and an empty citation
/// list.
/// </para>
/// </remarks>
public sealed record AskQuestionResponse(
    string Answer,
    IReadOnlyList<AnswerCitation> Citations,
    int RetrievedChunkCount,
    TokenUsage? TokenUsage,
    DateTimeOffset AnsweredAtUtc);
