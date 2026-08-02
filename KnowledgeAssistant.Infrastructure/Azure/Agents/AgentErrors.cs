using KnowledgeAssistant.Domain.Common;

namespace KnowledgeAssistant.Infrastructure.Azure.Agents;

/// <summary>
/// The failures the agent adapter can report.
/// </summary>
/// <remarks>
/// Generic descriptions and all <see cref="ErrorType.Failure"/>, as elsewhere: the
/// messages Azure returns carry project, agent, and deployment names that belong
/// in a log rather than an HTTP response, and nothing here is something the caller
/// could have avoided by asking differently.
/// </remarks>
internal static class AgentErrors
{
    /// <summary>The agent run failed and retries did not recover it.</summary>
    internal static readonly Error RunFailed = Error.Failure(
        "Agent.RunFailed",
        "An answer could not be generated.");

    /// <summary>The project was still rate limited after retries.</summary>
    /// <remarks>
    /// Separated for the same reason as the chat and embedding equivalents:
    /// nothing is broken, the deployment is simply too small for the load, and the
    /// fix is quota rather than debugging. Folding it into the general failure
    /// would hide a capacity problem inside a bucket that reads as defects.
    /// </remarks>
    internal static readonly Error RateLimited = Error.Failure(
        "Agent.RateLimited",
        "The answering service is currently rate limited. Try again shortly.");

    /// <summary>The application could not authenticate to Azure AI Foundry.</summary>
    internal static readonly Error AuthenticationFailed = Error.Failure(
        "Agent.AuthenticationFailed",
        "Answering is currently unavailable.");

    /// <summary>The agent produced no usable text.</summary>
    /// <remarks>
    /// Covers an empty run and a content-filter refusal alike. Both leave nothing
    /// to show the caller, and returning an empty string as a success would
    /// present "the agent declined" as an answer to the question.
    /// </remarks>
    internal static readonly Error NoAnswerGenerated = Error.Failure(
        "Agent.NoAnswerGenerated",
        "The answering service did not return an answer.");

    /// <summary>The agent could not be created or resolved in the project.</summary>
    /// <remarks>
    /// Distinct from <see cref="RunFailed"/> because the remedies have nothing in
    /// common. A run failure is usually transient or a bad question; this one is
    /// almost always a missing role assignment on the Foundry project, a deleted
    /// agent, or a pinned version that no longer exists — none of which a retry
    /// will fix.
    /// </remarks>
    internal static readonly Error ProvisioningFailed = Error.Failure(
        "Agent.ProvisioningFailed",
        "Answering is currently unavailable.");
}
