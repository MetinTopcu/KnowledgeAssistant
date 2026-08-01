using KnowledgeAssistant.Domain.Common;

namespace KnowledgeAssistant.Infrastructure.Azure.OpenAI;

/// <summary>
/// The failures the chat adapter can report.
/// </summary>
/// <remarks>
/// Generic descriptions and all <see cref="ErrorType.Failure"/>, as elsewhere:
/// the messages Azure returns carry resource and deployment names that belong in
/// a log rather than an HTTP response, and nothing here is something the caller
/// could have avoided by asking differently.
/// </remarks>
internal static class ChatErrors
{
    /// <summary>The completion request failed and retries did not recover it.</summary>
    internal static readonly Error CompletionFailed = Error.Failure(
        "Chat.CompletionFailed",
        "An answer could not be generated.");

    /// <summary>The deployment was still rate limited after retries.</summary>
    /// <remarks>
    /// Separated for the same reason as the embedding equivalent: nothing is
    /// broken, the deployment is simply too small for the load, and the fix is
    /// quota rather than debugging. Folding it into the general failure would
    /// hide a capacity problem inside a bucket that reads as defects.
    /// </remarks>
    internal static readonly Error RateLimited = Error.Failure(
        "Chat.RateLimited",
        "The answering service is currently rate limited. Try again shortly.");

    /// <summary>The application could not authenticate to Azure OpenAI.</summary>
    internal static readonly Error AuthenticationFailed = Error.Failure(
        "Chat.AuthenticationFailed",
        "Answering is currently unavailable.");

    /// <summary>The model returned no usable text.</summary>
    /// <remarks>
    /// Covers an empty completion and a content-filter refusal alike. Both leave
    /// nothing to show the caller, and returning an empty string as a success
    /// would present "the model declined" as an answer to the question.
    /// </remarks>
    internal static readonly Error NoAnswerGenerated = Error.Failure(
        "Chat.NoAnswerGenerated",
        "The answering service did not return an answer.");
}
