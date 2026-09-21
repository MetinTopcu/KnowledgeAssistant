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
/// Entra ID, sharing the credential configured once in <c>Azure:Credential</c>.
/// The data-plane role is <c>Azure AI User</c> on the Foundry account, which
/// also covers Document Intelligence and the agent.
/// </para>
/// <para>
/// <b>The endpoint is required.</b> Unlike Document Intelligence, which falls
/// back to a local extractor, there is no offline way to produce an embedding.
/// An unconfigured endpoint is therefore a misconfiguration rather than a choice,
/// and the process refuses to start.
/// </para>
/// </remarks>
public sealed class AzureOpenAIOptions : IValidatableObject
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

    /// <summary>The name of the deployed chat model.</summary>
    /// <remarks>
    /// A deployment name, not a model name — the same distinction that trips up
    /// the embedding setting. Required: unlike text extraction there is no local
    /// fallback for generating an answer, so an unset value is a
    /// misconfiguration rather than a choice.
    /// </remarks>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Azure:AiFoundry:ChatDeploymentName must be configured.")]
    public string ChatDeploymentName { get; init; } = string.Empty;

    /// <summary>The ceiling on tokens generated per answer.</summary>
    /// <remarks>
    /// Bounds cost and latency per question. An answer that hits the ceiling is
    /// returned truncated rather than discarded — a partial grounded answer is
    /// usually more useful than none — and the truncation is logged, because the
    /// remedy is to raise this value rather than to retry.
    /// </remarks>
    [Range(64, 16_384, ErrorMessage = "Azure:AiFoundry:ChatMaxOutputTokens must be between 64 and 16384.")]
    public int ChatMaxOutputTokens { get; init; } = 800;

    /// <summary>
    /// The sampling temperature for answers, or unset to send none and use the
    /// model's default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Unset by default because the chosen model rejects anything else.</b>
    /// Reasoning deployments such as <c>gpt-5-mini</c> accept only their default
    /// temperature: a request carrying <c>0</c> was answered
    /// <c>400 unsupported_value</c> ("Only the default (1) value is supported").
    /// Omitting the parameter works for every model, so it is the only default
    /// that does not depend on which deployment is configured.
    /// </para>
    /// <para>
    /// For a non-reasoning deployment, <c>0</c> is the right value for grounded
    /// question answering: the same question over the same corpus should produce
    /// the same answer.
    /// </para>
    /// </remarks>
    [Range(0.0, 2.0, ErrorMessage = "Azure:AiFoundry:ChatTemperature must be between 0.0 and 2.0.")]
    public double? ChatTemperature { get; init; }

    /// <summary>The underlying model behind the embedding deployment.</summary>
    /// <remarks>
    /// Distinct from <see cref="EmbeddingDeploymentName"/>. Embedding generation
    /// addresses the deployment; this names what that deployment serves, so that
    /// <see cref="EmbeddingDimensions"/> can be checked against it at startup.
    /// The default is the model the verified deployment serves.
    /// </remarks>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Azure:AiFoundry:EmbeddingModelName must be configured.")]
    public string EmbeddingModelName { get; init; } = "text-embedding-3-large";

    /// <summary>
    /// The vector length the deployed model produces, and the dimension the
    /// retrieval index is created with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not a request — the application never asks for a reduced dimension — but
    /// an assertion and a schema input. Each returned vector is checked against
    /// it, and <c>knowledge-chunks</c> is created with exactly this many
    /// dimensions. Vector dimensions cannot be changed on an existing index, so
    /// changing this value means recreating that index and re-ingesting.
    /// </para>
    /// <para>
    /// 3072 is what <c>text-embedding-3-large</c> returns (observed from the live
    /// deployment). For a known model the startup check below rejects any other
    /// value, because those vectors would fail every write to the index.
    /// </para>
    /// </remarks>
    [Range(1, 16384, ErrorMessage = "Azure:AiFoundry:EmbeddingDimensions must be between 1 and 16384.")]
    public int EmbeddingDimensions { get; init; } = 3072;

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

    /// <summary>
    /// The native output length of embedding models whose size is fixed here,
    /// because the application requests no reduced dimension.
    /// </summary>
    /// <remarks>
    /// Only models listed here are checked. An unlisted model is accepted with
    /// whatever dimension is configured, and the per-vector check at run time
    /// remains the backstop.
    /// </remarks>
    internal static readonly IReadOnlyDictionary<string, int> KnownEmbeddingDimensions =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["text-embedding-3-small"] = 1536,
            ["text-embedding-3-large"] = 3072,
            ["text-embedding-ada-002"] = 1536,
        };

    /// <summary>
    /// Rejects a dimension the configured model cannot produce.
    /// </summary>
    /// <remarks>
    /// Caught at startup rather than on the first upload, where it would surface
    /// as <c>Embedding.DimensionMismatch</c> after a blob had already been
    /// written — or as a retrieval index created with the wrong dimension, which
    /// must be deleted before anything can be ingested.
    /// </remarks>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (KnownEmbeddingDimensions.TryGetValue(EmbeddingModelName, out int expected) &&
            EmbeddingDimensions != expected)
        {
            yield return new ValidationResult(
                $"Azure:AiFoundry:EmbeddingDimensions is {EmbeddingDimensions}, but {EmbeddingModelName} " +
                $"produces {expected}-dimensional vectors. Set it to {expected}, or correct " +
                "Azure:AiFoundry:EmbeddingModelName to the model the embedding deployment serves.",
                [nameof(EmbeddingDimensions), nameof(EmbeddingModelName)]);
        }
    }
}
