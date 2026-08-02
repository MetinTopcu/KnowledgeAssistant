using KnowledgeAssistant.Application.Interfaces;

namespace KnowledgeAssistant.Application.Queries.Documents.AskAgent;

/// <summary>
/// One passage the agent retrieved while answering.
/// </summary>
/// <param name="ReferenceNumber">
/// The number this passage was given when the agent was shown it. Markers such as
/// <c>[2]</c> in the answer refer to it.
/// </param>
/// <param name="ChunkId">The chunk's identifier in the retrieval index.</param>
/// <param name="DocumentId">The document the chunk came from.</param>
/// <param name="ChunkOrder">The chunk's position in that document.</param>
/// <param name="Text">The chunk's text, exactly as the agent saw it.</param>
/// <param name="BlobUri">The absolute location of the source document.</param>
/// <param name="Score">The relevance score that selected this passage.</param>
/// <remarks>
/// <para>
/// <b>Why this is not <c>AnswerCitation</c> from the retrieval slice.</b> The two
/// records have the same fields and different meanings, which is the worst kind of
/// type to share. There, <c>ReferenceNumber</c> is a position in a prompt the
/// handler built, so it is known before the model runs and every retrieved chunk
/// has one. Here it is the order in which the agent chose to look things up: it
/// depends on searches nobody scripted, it is not knowable in advance, and a
/// second search can return a passage the first already produced. Merging them
/// would force one of those meanings onto the other the first time either
/// changed.
/// </para>
/// <para>
/// <b>Duplicates are collapsed, and the first sighting wins the number.</b> An
/// agent that searches twice with similar wording will see the same passage twice;
/// numbering it twice would let a single source appear to corroborate itself.
/// </para>
/// </remarks>
public sealed record AgentCitation(
    int ReferenceNumber,
    Guid ChunkId,
    Guid DocumentId,
    int ChunkOrder,
    string Text,
    Uri BlobUri,
    double Score);

/// <summary>
/// The agent's answer, with the evidence it gathered.
/// </summary>
/// <param name="Answer">The generated answer.</param>
/// <param name="Citations">
/// Every passage the agent retrieved, numbered as the agent saw them.
/// </param>
/// <param name="SearchCount">
/// How many searches the agent ran. Zero means it answered without consulting the
/// corpus at all.
/// </param>
/// <param name="TokenUsage">
/// What the exchange cost in total, or <see langword="null"/> when the provider
/// reported nothing.
/// </param>
/// <param name="AnsweredAtUtc">When the answer was produced, in UTC.</param>
/// <remarks>
/// <para>
/// <b><paramref name="SearchCount"/> is the field a reader should look at first.</b>
/// Unlike the retrieval pipeline, where a zero chunk count forces an honest "I
/// found nothing", an agent that never searched still produces a confident,
/// well-written answer — from its training data, about documents it did not open.
/// This number is the only thing in the response that distinguishes that case, and
/// it is why the field is reported rather than merely logged.
/// </para>
/// <para>
/// A zero <paramref name="SearchCount"/> is not treated as an error. The agent is
/// permitted to decline a question that is not about the corpus at all, and
/// failing the request would turn a correct refusal into a 500.
/// </para>
/// </remarks>
public sealed record AskAgentResponse(
    string Answer,
    IReadOnlyList<AgentCitation> Citations,
    int SearchCount,
    TokenUsage? TokenUsage,
    DateTimeOffset AnsweredAtUtc);
