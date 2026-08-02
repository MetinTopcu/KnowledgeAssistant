using System.ComponentModel.DataAnnotations;

namespace KnowledgeAssistant.Infrastructure.Azure.Agents;

/// <summary>
/// Configuration for the Azure AI Foundry agent, bound from the
/// <c>Azure:AiFoundry:Agent</c> section.
/// </summary>
/// <remarks>
/// <para>
/// <b>A subsection of <c>Azure:AiFoundry</c> rather than a section of its own.</b>
/// The agent runs on the same AI Foundry resource that serves chat and
/// embeddings, so it has the same endpoint and the same throttling; a sibling
/// <c>Azure:Agent</c> section would carry a second endpoint key describing one
/// resource, and the two would eventually disagree. The endpoint and the retry
/// budget are read from the parent — see <see cref="OpenAI.AzureOpenAIOptions"/> —
/// and only what is genuinely agent-specific lives here.
/// </para>
/// <para>
/// <b>Nothing here is required.</b> Every value has a working default, so adding
/// the agent does not add a setting a deployment must discover. That is a
/// deliberate contrast with the endpoint and deployment names, which have no
/// sensible default and correctly fail the process at startup when absent.
/// </para>
/// <para>
/// <b>No API key, as everywhere else.</b> Authentication is Entra ID via
/// <c>DefaultAzureCredential</c>, sharing the credential configured once in
/// <c>Azure:Credential</c>. The data-plane role is <c>Azure AI User</c> on the
/// Foundry project; provisioning an agent version additionally requires
/// <c>Azure AI Project Manager</c>.
/// </para>
/// </remarks>
public sealed class FoundryAgentOptions : IValidatableObject
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Azure:AiFoundry:Agent";

    /// <summary>
    /// The Foundry <em>project</em> endpoint, or empty to reuse
    /// <c>Azure:AiFoundry:Endpoint</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>These are frequently not the same URL, which is the reason this setting
    /// exists.</b> The OpenAI-compatible surface that serves chat and embeddings is
    /// addressed at the account — <c>https://contoso.services.ai.azure.com/</c> —
    /// while agents belong to a project inside that account and are addressed at
    /// <c>https://contoso.services.ai.azure.com/api/projects/my-project</c>. An
    /// account with exactly one project often answers on both, which is precisely
    /// what makes the difference easy to miss until a second project appears.
    /// </para>
    /// <para>
    /// Defaulting to the parent endpoint keeps the single-project case free of
    /// another setting. Set it explicitly when the account hosts more than one
    /// project, or the agent resolves against whichever one Azure picks.
    /// </para>
    /// </remarks>
    public string ProjectEndpoint { get; init; } = string.Empty;

    /// <summary>The agent's name within the Foundry project.</summary>
    /// <remarks>
    /// Identifies the agent, so two deployments sharing a project and a name share
    /// an agent. Give a staging deployment its own name unless that is what was
    /// wanted.
    /// </remarks>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Azure:AiFoundry:Agent:Name must be configured.")]
    public string Name { get; init; } = "knowledge-assistant";

    /// <summary>
    /// A specific agent version to use, or empty to resolve one at first use.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Set this in production.</b> Pinning makes the agent's instructions and
    /// tool schema part of the deployment rather than something the running
    /// process decides, so a change to either is reviewed and rolled out like any
    /// other change. It also removes the write permission the alternative needs.
    /// </para>
    /// <para>
    /// Left empty, the adapter provisions on first use: it looks for an existing
    /// version whose definition matches, and creates one only when none does. That
    /// is what makes the service usable from a clean project without a separate
    /// provisioning step.
    /// </para>
    /// </remarks>
    public string Version { get; init; } = string.Empty;

    /// <summary>Whether an agent version is pinned rather than resolved.</summary>
    public bool IsVersionPinned => !string.IsNullOrWhiteSpace(Version);

    /// <summary>
    /// The chat deployment the agent reasons with, or empty to reuse
    /// <c>Azure:AiFoundry:ChatDeploymentName</c>.
    /// </summary>
    /// <remarks>
    /// Separable because the two workloads differ: the retrieval pipeline writes
    /// prose from evidence it was handed, while the agent must decide what to
    /// search for and when to stop, which a smaller model does noticeably worse.
    /// Defaulting to the same deployment keeps the common case free of a second
    /// setting.
    /// </remarks>
    public string ModelDeploymentName { get; init; } = string.Empty;

    /// <summary>The sampling temperature the agent answers at.</summary>
    /// <remarks>
    /// Zero for the same reason the chat deployment uses zero: a grounded answer
    /// should be reproducible, and sampling variation in a factual lookup is noise
    /// rather than creativity. It matters more here — temperature also perturbs
    /// the agent's decision about <i>what to search for</i>, so raising it makes
    /// the retrieval itself non-reproducible, not just the wording.
    /// </remarks>
    [Range(0.0, 2.0, ErrorMessage = "Azure:AiFoundry:Agent:Temperature must be between 0.0 and 2.0.")]
    public double Temperature { get; init; }

    /// <summary>
    /// The most rounds of tool calls permitted before the agent is stopped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The hard cost ceiling, and the one setting here worth understanding. An
    /// agent decides for itself when it has searched enough, and a model that
    /// keeps deciding "not yet" would otherwise loop until something else broke —
    /// spending a completion and a retrieval each time, against a question the
    /// caller is still waiting on.
    /// </para>
    /// <para>
    /// Four is generous for a corpus lookup: the observed pattern is one search,
    /// occasionally a second with reworded terms. Reaching this bound is logged as
    /// a warning rather than failed, because a partial answer from three searches
    /// is worth more to the caller than an error.
    /// </para>
    /// </remarks>
    [Range(1, 10, ErrorMessage = "Azure:AiFoundry:Agent:MaxToolIterations must be between 1 and 10.")]
    public int MaxToolIterations { get; init; } = 4;

    /// <summary>
    /// The upper bound on characters of retrieved text returned from one search.
    /// </summary>
    /// <remarks>
    /// The counterpart of the retrieval pipeline's context budget, and a backstop
    /// rather than the usual limiter — the requested source count normally binds
    /// first. It exists because chunk size is configurable and the source count is
    /// caller-supplied, so their product is not something this code can assume
    /// stays small. Sources arrive in rank order, so trimming drops the least
    /// relevant rather than an arbitrary slice.
    /// </remarks>
    [Range(1_000, 200_000, ErrorMessage = "Azure:AiFoundry:Agent:MaxSearchResultCharacters must be between 1000 and 200000.")]
    public int MaxSearchResultCharacters { get; init; } = 24_000;

    /// <summary>
    /// Validates the project endpoint only when one has been supplied.
    /// </summary>
    /// <remarks>
    /// A conditional rule, so it cannot be an attribute — the same arrangement
    /// <see cref="DocumentIntelligence.DocumentIntelligenceOptions"/> uses, and for
    /// the same reason. <c>[Url]</c> rejects the empty string, which here
    /// legitimately means "use the parent AI Foundry endpoint", so the attribute
    /// would fail every deployment that did not need this setting. Omitting the
    /// check entirely is worse: a typo would bind silently and surface as an agent
    /// that cannot be resolved, on the first question, in production.
    /// </remarks>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(ProjectEndpoint) &&
            !Uri.TryCreate(ProjectEndpoint, UriKind.Absolute, out _))
        {
            yield return new ValidationResult(
                "Azure:AiFoundry:Agent:ProjectEndpoint must be an absolute URL when it is set.",
                [nameof(ProjectEndpoint)]);
        }
    }
}
