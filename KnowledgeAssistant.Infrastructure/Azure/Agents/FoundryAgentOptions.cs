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
/// <b>Two values are required.</b> <see cref="ProjectEndpoint"/> always, because
/// agents exist only inside a project and no other endpoint serves them; and
/// <see cref="Version"/> outside Development, where the agent must be pinned
/// (enforced by <c>FoundryAgentOptionsValidator</c>, which needs the host
/// environment). Everything else has a working default.
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
    /// The Foundry <em>project</em> endpoint, for example
    /// <c>https://contoso.services.ai.azure.com/api/projects/my-project</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Required, and never the account endpoint.</b> The OpenAI-compatible
    /// surface that serves chat and embeddings is addressed at the account —
    /// <c>https://contoso.services.ai.azure.com/</c> — while agents belong to a
    /// project inside that account. Against a live account with exactly one
    /// project, the account endpoint answered 404 to every agent call, so there
    /// is no fallback: an empty value, or one without an
    /// <c>/api/projects/&lt;name&gt;</c> path, fails at startup instead of on the
    /// first question.
    /// </para>
    /// </remarks>
    [Required(AllowEmptyStrings = false, ErrorMessage =
        "Azure:AiFoundry:Agent:ProjectEndpoint must be configured: the Foundry project endpoint, " +
        "https://<account>.services.ai.azure.com/api/projects/<project>.")]
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
    /// The agent version this deployment answers with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Required outside Development.</b> Pinning makes the agent's instructions
    /// and tool schema part of the deployment rather than something the running
    /// process decides, so a change to either is reviewed and rolled out like any
    /// other change. It also removes the write permission provisioning needs.
    /// </para>
    /// <para>
    /// <b>A pinned version is checked, not trusted.</b> On first use the adapter
    /// reads that version and compares its stored definition fingerprint with the
    /// one this build computes. A mismatch — different instructions, tool schema,
    /// model deployment, or temperature — fails with a log naming both
    /// fingerprints, because the agent would otherwise run a definition this
    /// code was not written against.
    /// </para>
    /// <para>
    /// Left empty (Development only), the adapter provisions on first use: it
    /// reuses a version whose fingerprint matches, and creates one only when none
    /// does. The version it settles on is logged; that is the value to pin.
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

    /// <summary>
    /// The sampling temperature the agent answers at, or unset to leave it out
    /// of the agent definition and use the model's default.
    /// </summary>
    /// <remarks>
    /// Unset by default for the reason <c>Azure:AiFoundry:ChatTemperature</c> is:
    /// reasoning models such as <c>gpt-5-mini</c> reject any value but their
    /// default. With a non-reasoning model, <c>0</c> is preferable — temperature
    /// also perturbs the agent's decision about <i>what to search for</i>, so
    /// raising it makes the retrieval itself non-reproducible. Part of the
    /// definition fingerprint: changing it requires a new pinned version.
    /// </remarks>
    [Range(0.0, 2.0, ErrorMessage = "Azure:AiFoundry:Agent:Temperature must be between 0.0 and 2.0.")]
    public double? Temperature { get; init; }

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
    /// Validates that the project endpoint addresses a project.
    /// </summary>
    /// <remarks>
    /// Emptiness is left to <c>[Required]</c>, so a missing value reports once.
    /// The shape check is what catches the account endpoint pasted where the
    /// project endpoint belongs — the mistake that produced a 404 on every agent
    /// call rather than a startup error.
    /// </remarks>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(ProjectEndpoint))
        {
            yield break;
        }

        if (!TryGetProjectName(ProjectEndpoint, out _))
        {
            yield return new ValidationResult(
                "Azure:AiFoundry:Agent:ProjectEndpoint must be an absolute https URL of the form " +
                "https://<account>.services.ai.azure.com/api/projects/<project>; the account endpoint " +
                "does not serve agents.",
                [nameof(ProjectEndpoint)]);
        }
    }

    /// <summary>
    /// Extracts the project name from a project endpoint.
    /// </summary>
    internal static bool TryGetProjectName(string endpoint, out string projectName)
    {
        projectName = string.Empty;

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length != 3 ||
            !string.Equals(segments[0], "api", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(segments[1], "projects", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        projectName = segments[2];
        return true;
    }
}
