using KnowledgeAssistant.Application.Abstractions;

namespace KnowledgeAssistant.Application.Queries.Documents.Ask;

/// <summary>
/// Asks a question of the ingested corpus.
/// </summary>
/// <param name="Question">The user's question, in natural language.</param>
/// <param name="TopK">
/// How many chunks to retrieve as grounding. Higher values widen the evidence and
/// enlarge the prompt.
/// </param>
/// <remarks>
/// <para>
/// <b>An <see cref="IQuery{TResponse}"/>, not a command.</b> Answering changes
/// nothing: no blob is written, no index is touched, and running it twice
/// produces the same effect on the system as running it once. That it costs money
/// and is not deterministic does not make it a write.
/// </para>
/// <para>
/// <b>No conversation identifier, and no history.</b> Every question is answered
/// from the corpus alone. Multi-turn memory is excluded deliberately — it is a
/// decision with real consequences for cost, for prompt size, and for what
/// personal data the service ends up retaining, and it should be taken on its own
/// merits rather than inherited from a field nobody meant to add.
/// </para>
/// <para>
/// <b>Why <paramref name="TopK"/> is on the request.</b> How much evidence is
/// enough varies with the question — a narrow lookup wants two chunks, a
/// synthesis wants ten — and only the caller knows which it is asking. The
/// validator bounds it so the choice cannot become a way to inflate a prompt
/// without limit.
/// </para>
/// </remarks>
public sealed record AskQuestionQuery(
    string Question,
    int TopK) : IQuery<AskQuestionResponse>;
