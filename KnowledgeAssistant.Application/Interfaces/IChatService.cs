using KnowledgeAssistant.Domain.Common;

namespace KnowledgeAssistant.Application.Interfaces;

/// <summary>
/// Generates an answer from a set of chat messages.
/// </summary>
/// <remarks>
/// <para>
/// An outbound port, like every other interface in this folder. Nothing in the
/// signature names a vendor, a model, a deployment, a temperature, or a token
/// budget — those are deployment concerns, and a caller has no basis to choose
/// them.
/// </para>
/// <para>
/// <b>Deliberately not "ask a question".</b> This port knows nothing about
/// retrieval, grounding, citations, or prompts. It takes messages and returns
/// text. Everything that makes an answer <i>grounded</i> lives in the use case
/// that builds those messages, where it can be read, changed, and tested without
/// a model in the loop.
/// </para>
/// <para>
/// <b>Stateless by construction.</b> Each call carries every message it needs;
/// the implementation retains nothing between calls. Conversation history would
/// be a property of the caller's messages, not of this port — which is what keeps
/// the decision to add it a deliberate one.
/// </para>
/// </remarks>
public interface IChatService
{
    /// <summary>
    /// Sends <paramref name="messages"/> to the model and returns its answer.
    /// </summary>
    /// <param name="messages">
    /// The conversation to complete, in order. Must contain at least one message.
    /// </param>
    /// <param name="cancellationToken">Cancelled when the caller disconnects.</param>
    /// <returns>The generated answer and its token usage, or a failure.</returns>
    Task<Result<ChatCompletionResult>> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken);
}
