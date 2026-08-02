using KnowledgeAssistant.Application.Abstractions;

namespace KnowledgeAssistant.Application.Queries.Documents.AskAgent;

/// <summary>
/// Asks the agent a question about the ingested corpus.
/// </summary>
/// <param name="Question">The user's question, in natural language.</param>
/// <param name="MaxSources">
/// The most passages any one of the agent's searches may return.
/// </param>
/// <remarks>
/// <para>
/// <b>A separate slice from <c>AskQuestionQuery</c>, not a flag on it.</b> The two
/// answer the same kind of question by different means and with different
/// guarantees: the retrieval pipeline always performs exactly one search and costs
/// one completion, while the agent may search several times or none, and its cost
/// is bounded only by configuration. Those are different products for a caller —
/// different latency, different bill, different failure modes — and a boolean on
/// one request would hide that behind a parameter nobody reads.
/// </para>
/// <para>
/// <b>An <see cref="IQuery{TResponse}"/>, for the same reason the other one is.</b>
/// Answering writes nothing: no blob, no index. That it costs money and is not
/// deterministic does not make it a write.
/// </para>
/// <para>
/// <b>No conversation identifier.</b> Every question is answered on its own, as in
/// the retrieval slice. Multi-turn is excluded deliberately rather than by
/// omission — see <see cref="Interfaces.IAgentService"/>.
/// </para>
/// </remarks>
public sealed record AskAgentQuery(
    string Question,
    int MaxSources) : IQuery<AskAgentResponse>;
