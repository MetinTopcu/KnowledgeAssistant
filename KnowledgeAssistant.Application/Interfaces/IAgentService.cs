using KnowledgeAssistant.Domain.Common;

namespace KnowledgeAssistant.Application.Interfaces;

/// <summary>
/// Answers a question by reasoning over the corpus, deciding for itself what to
/// retrieve.
/// </summary>
/// <remarks>
/// <para>
/// An outbound port, like every other interface in this folder. Nothing in the
/// signature names a vendor, a model, an agent identifier, a tool, or a protocol.
/// A caller hands over a question and receives an answer with the evidence behind
/// it.
/// </para>
/// <para>
/// <b>How this differs from <see cref="IChatService"/>, and why both exist.</b>
/// <see cref="IChatService"/> completes messages the caller has already grounded:
/// the use case retrieves first, builds the prompt, and the model only writes
/// prose. This port inverts that control. The implementation is given the ability
/// to search and decides <i>whether</i>, <i>how often</i>, and <i>with what
/// query</i> to use it — so a question that needs two lookups gets two, and one
/// that needs a reformulated query gets that too. Neither subsumes the other: the
/// first is cheap, single-shot, and completely predictable; the second is none of
/// those and answers questions the first cannot.
/// </para>
/// <para>
/// <b>The retrieval it performs is this system's own.</b> Whatever search the
/// implementation runs goes through the same embedding model and the same index
/// the corpus was built with. That is a requirement of the port rather than an
/// accident of the adapter: vectors from a second model are not comparable to the
/// corpus, and a mismatch produces confident nonsense instead of an error.
/// </para>
/// <para>
/// <b>Stateless between calls.</b> Each question is answered on its own, and the
/// implementation retains no conversation. Multi-turn memory is a decision with
/// consequences for cost, prompt size, and what personal data the service
/// retains; it is not inherited by leaving a field on this contract.
/// </para>
/// </remarks>
public interface IAgentService
{
    /// <summary>
    /// Answers <paramref name="question"/>, retrieving from the corpus as needed.
    /// </summary>
    /// <param name="question">The question and the bound on evidence per lookup.</param>
    /// <param name="cancellationToken">Cancelled when the caller disconnects.</param>
    /// <returns>The answer and what it was built from, or a failure.</returns>
    Task<Result<AgentAnswer>> AskAsync(
        AgentQuestion question,
        CancellationToken cancellationToken);
}

/// <summary>
/// A question put to the agent.
/// </summary>
/// <param name="Question">The user's question, in natural language.</param>
/// <param name="MaxSources">
/// The most passages any single retrieval may return. A ceiling on one lookup,
/// not on the whole answer — an agent that searches three times may consult up to
/// three times this many.
/// </param>
/// <remarks>
/// <paramref name="MaxSources"/> bounds the prompt rather than the reasoning. It
/// is on the request for the same reason <c>TopK</c> is on the ordinary question:
/// how much evidence is enough varies with what is being asked, and only the
/// caller knows which kind of question it is.
/// </remarks>
public sealed record AgentQuestion(string Question, int MaxSources);

/// <summary>
/// The agent's answer, with everything needed to judge it.
/// </summary>
/// <param name="Content">The generated answer.</param>
/// <param name="ConsultedSources">
/// Every passage the agent retrieved, in the order it first saw them, with
/// duplicates across repeated searches collapsed.
/// </param>
/// <param name="SearchCount">
/// How many times the agent searched the corpus. Zero means it answered without
/// looking anything up.
/// </param>
/// <param name="Usage">
/// What the whole exchange cost, summed across every model call the agent made,
/// or <see langword="null"/> when the provider reported nothing.
/// </param>
/// <remarks>
/// <para>
/// <b><paramref name="SearchCount"/> is the field that makes an agent
/// auditable.</b> An agent is free to answer without searching, and when it does
/// so the answer came from the model's training rather than from the corpus —
/// which is indistinguishable from a grounded answer by reading it. Reporting the
/// count is what lets a caller, a test, or a dashboard tell the two apart.
/// </para>
/// <para>
/// <b><paramref name="Usage"/> is a sum, not a single call's usage.</b> An agent
/// that searches twice makes three model calls, and reporting only the last would
/// understate the bill by however much the reasoning cost — precisely the part
/// that distinguishes this from an ordinary completion.
/// </para>
/// </remarks>
public sealed record AgentAnswer(
    string Content,
    IReadOnlyList<ChunkSearchResult> ConsultedSources,
    int SearchCount,
    TokenUsage? Usage);
