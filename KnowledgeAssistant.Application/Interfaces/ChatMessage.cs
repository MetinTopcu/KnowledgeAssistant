namespace KnowledgeAssistant.Application.Interfaces;

/// <summary>The author of a chat message.</summary>
public enum ChatRole
{
    /// <summary>Instructions that govern how the model should answer.</summary>
    System = 0,

    /// <summary>Input from the caller.</summary>
    User = 1,

    /// <summary>A previous answer from the model.</summary>
    /// <remarks>
    /// Declared for completeness of the role set, and deliberately unused: this
    /// system sends one system message and one user message per call and keeps no
    /// conversation. Multi-turn is a separate decision with its own cost and
    /// privacy consequences.
    /// </remarks>
    Assistant = 2,
}

/// <summary>
/// One message in a chat completion request.
/// </summary>
/// <param name="Role">Who the message is from.</param>
/// <param name="Content">The message text.</param>
/// <remarks>
/// A vendor-neutral stand-in for the SDK's message hierarchy. Taking the SDK's
/// own type here would put <c>OpenAI.Chat</c> into the Application layer's public
/// surface — the coupling the ports exist to prevent — and would name a vendor in
/// a contract that any chat provider should be able to satisfy.
/// </remarks>
public sealed record ChatMessage(ChatRole Role, string Content);
