using System.ComponentModel.DataAnnotations;

namespace KnowledgeAssistant.Infrastructure.Azure.OpenAI;

/// <summary>
/// Configuration for the Azure OpenAI embedding adapter, bound from the
/// <c>Azure:AiFoundry</c> section.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it binds <c>Azure:AiFoundry</c> and not a new <c>Azure:OpenAI</c>
/// section.</b> That section already carries <c>Endpoint</c>,
/// <c>EmbeddingDeploymentName</c>, and <c>EmbeddingDimensions</c>, documented
/// since the scaffold and reflected in <c>CONFIGURATION.md</c>. Azure AI Foundry
/// is the resource that hosts these deployments and serves the OpenAI API, so a
/// second endpoint key would describe the same resource twice — and the two
/// would eventually disagree.
/// </para>
/// <para>
/// <b>No API key.</b> Azure OpenAI issues keys; none is used. Authentication is
/// Entra ID via <c>DefaultAzureCredential</c>, sharing the credential configured
/// once in <c>Azure:Credential</c>. The data-plane role is
/// <c>Cognitive Services OpenAI User</c>.
/// </para>
/// <para>
/// <b>The endpoint is required.</b> Unlike Document Intelligence, which falls
/// back to a local extractor, there is no offline way to produce an embedding.
/// An unconfigured endpoint is therefore a misconfiguration rather than a choice,
/// and the process refuses to start.
/// </para>
/// </remarks>
public sealed class AzureOpenAIOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Azure:AiFoundry";

    /// <summary>
    /// The AI Foundry endpoint, for example
    /// <c>https://contoso.services.ai.azure.com/</c>.
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Azure:AiFoundry:Endpoint must be configured.")]
    [Url(ErrorMessage = "Azure:AiFoundry:Endpoint must be an absolute URL.")]
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>The name of the deployed embedding model.</summary>
    /// <remarks>
    /// A <em>deployment</em> name, not a model name. They are frequently
    /// different, and using the model name where Azure expects the deployment is
    /// the most common cause of a 404 from an otherwise correct configuration.
    /// </remarks>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Azure:AiFoundry:EmbeddingDeploymentName must be configured.")]
    public string EmbeddingDeploymentName { get; init; } = string.Empty;

    /// <summary>The underlying model behind the embedding deployment.</summary>
    /// <remarks>
    /// Distinct from <see cref="EmbeddingDeploymentName"/>, and needed because
    /// Azure AI Search's integrated vectorizer validates the model rather than
    /// the deployment: it checks that the dimensions an index declares are
    /// achievable by that model before it will accept the index definition. It is
    /// unused by embedding generation itself, which addresses the deployment.
    /// </remarks>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Azure:AiFoundry:EmbeddingModelName must be configured.")]
    public string EmbeddingModelName { get; init; } = "text-embedding-3-small";

    /// <summary>
    /// The vector length the deployed model is expected to produce, or 0 to skip
    /// the check.
    /// </summary>
    /// <remarks>
    /// Not a request — this SDK version exposes no way to ask for a dimension
    /// count — but an assertion. If the deployment is repointed at a different
    /// model, the vectors silently change length, and every index built from a
    /// mixture of lengths is unusable. Checking each returned vector converts
    /// that from a slow, confusing degradation into an immediate, named failure.
    /// </remarks>
    [Range(0, 16384, ErrorMessage = "Azure:AiFoundry:EmbeddingDimensions must be between 0 and 16384.")]
    public int EmbeddingDimensions { get; init; } = 1536;

    /// <summary>The number of chunks sent in a single embedding request.</summary>
    /// <remarks>
    /// <para>
    /// The binding constraint is tokens per request, not items. Azure accepts
    /// large input arrays, but the deployment's tokens-per-minute quota is what
    /// actually rejects the call — and an 800-character chunk is roughly 200
    /// tokens, so a batch of 16 is about 3,200 tokens. That is comfortably inside
    /// every current deployment's per-request budget while still cutting request
    /// count by a factor of sixteen.
    /// </para>
    /// <para>
    /// Raising this trades fewer requests for a larger blast radius: one rejected
    /// batch fails every chunk in it.
    /// </para>
    /// </remarks>
    [Range(1, 2048, ErrorMessage = "Azure:AiFoundry:EmbeddingBatchSize must be between 1 and 2048.")]
    public int EmbeddingBatchSize { get; init; } = 16;

    /// <summary>The number of retries attempted after a transient failure.</summary>
    [Range(0, 10, ErrorMessage = "Azure:AiFoundry:MaxRetryAttempts must be between 0 and 10.")]
    public int MaxRetryAttempts { get; init; } = 4;

    /// <summary>The base delay for exponential backoff, in seconds.</summary>
    [Range(0.1, 60, ErrorMessage = "Azure:AiFoundry:RetryBaseDelaySeconds must be between 0.1 and 60.")]
    public double RetryBaseDelaySeconds { get; init; } = 1;

    /// <summary>The ceiling on any single backoff delay, in seconds.</summary>
    /// <remarks>
    /// Applies to a server-supplied <c>Retry-After</c> as well as to computed
    /// backoff. A rate-limited deployment can legitimately ask for a very long
    /// wait, and honouring it unbounded would hold a request thread and a
    /// caller's connection open far past any sensible timeout.
    /// </remarks>
    [Range(1, 300, ErrorMessage = "Azure:AiFoundry:RetryMaxDelaySeconds must be between 1 and 300.")]
    public double RetryMaxDelaySeconds { get; init; } = 30;
}
