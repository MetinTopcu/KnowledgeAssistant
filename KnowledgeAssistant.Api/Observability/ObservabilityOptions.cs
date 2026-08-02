using System.ComponentModel.DataAnnotations;

namespace KnowledgeAssistant.Api.Observability;

/// <summary>
/// Everything about telemetry that a deployment may decide.
/// </summary>
/// <remarks>
/// <para>
/// One section, bound and validated at startup like every other in this solution.
/// The alternative — reading environment variables at the point of use — is how a
/// service ends up exporting to the wrong workspace in one environment and
/// nowhere at all in another, with nothing failing to say so.
/// </para>
/// <para>
/// Every switch defaults to the safe production answer, so an appsettings.json
/// that says nothing about observability still produces a correctly instrumented
/// service that simply exports nowhere until a destination is configured.
/// </para>
/// </remarks>
public sealed class ObservabilityOptions
{
    /// <summary>The configuration section these bind from.</summary>
    public const string SectionName = "Observability";

    /// <summary>
    /// The <c>service.name</c> reported on every span, metric, and log.
    /// </summary>
    /// <remarks>
    /// The single most important attribute in the whole configuration: it is what
    /// a backend groups by, and two services sharing one name are indistinguishable
    /// forever afterwards.
    /// </remarks>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Observability:ServiceName must be configured.")]
    public string ServiceName { get; init; } = "knowledge-assistant-api";

    /// <summary>
    /// The build this instance is running, reported as <c>service.version</c>.
    /// </summary>
    /// <remarks>
    /// Left empty, the assembly's informational version is used. Supplying it from
    /// the pipeline is better: it is what lets "the p99 doubled" be answered with
    /// "at 14:02, which is when build 1.4.7 rolled out".
    /// </remarks>
    public string ServiceVersion { get; init; } = string.Empty;

    /// <summary>Whether distributed tracing is collected at all.</summary>
    public bool TracingEnabled { get; init; } = true;

    /// <summary>Whether metrics are collected at all.</summary>
    public bool MetricsEnabled { get; init; } = true;

    /// <summary>Whether log records are exported through OpenTelemetry.</summary>
    /// <remarks>
    /// Independent of Serilog, which keeps writing to its own sinks either way.
    /// This governs only whether a second copy is shipped to the telemetry backend.
    /// </remarks>
    public bool LoggingEnabled { get; init; } = true;

    /// <summary>
    /// The fraction of traces recorded, from 0 to 1.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One is right until it is expensive. The sampler is parent-based, so this
    /// applies to traces that start here; a request arriving with a sampled
    /// parent is always recorded, which is what keeps a sampled trace from
    /// arriving with holes in the middle.
    /// </para>
    /// <para>
    /// Note that sampling discards traces, not metrics. Request rates and
    /// latencies stay exact at any sampling ratio, because they are counted
    /// before the sampler runs — which is the reason to alert on metrics and
    /// investigate with traces, rather than the other way round.
    /// </para>
    /// </remarks>
    [Range(0.0, 1.0, ErrorMessage = "Observability:SamplingRatio must be between 0.0 and 1.0.")]
    public double SamplingRatio { get; init; } = 1.0;

    /// <summary>Instrumentation sources, each switchable.</summary>
    public InstrumentationOptions Instrumentation { get; init; } = new();

    /// <summary>Application Insights export.</summary>
    public AzureMonitorOptions AzureMonitor { get; init; } = new();

    /// <summary>OTLP export, for a local collector or a non-Azure backend.</summary>
    public OtlpOptions Otlp { get; init; } = new();
}

/// <summary>Which instrumentation libraries are active.</summary>
/// <remarks>
/// Individually switchable because they fail and cost differently. HTTP client
/// instrumentation on a service that talks to a chatty dependency can produce
/// more spans than the requests that caused them, and being able to turn one
/// source off without losing the rest is the difference between tuning telemetry
/// and disabling it.
/// </remarks>
public sealed class InstrumentationOptions
{
    /// <summary>Incoming HTTP requests: routes, status codes, durations.</summary>
    public bool AspNetCore { get; init; } = true;

    /// <summary>Outgoing <c>HttpClient</c> calls.</summary>
    public bool HttpClient { get; init; } = true;

    /// <summary>
    /// Azure SDK activity sources.
    /// </summary>
    /// <remarks>
    /// This is what turns "the upload was slow" into "the upload was slow because
    /// the third indexing batch was throttled and retried twice". The Azure SDKs
    /// suppress their own inner HTTP spans when this is collected, so it does not
    /// duplicate what HttpClient instrumentation reports.
    /// </remarks>
    public bool AzureSdk { get; init; } = true;

    /// <summary>Garbage collection, thread pool, and exception counters.</summary>
    public bool Runtime { get; init; } = true;
}

/// <summary>Application Insights export settings.</summary>
public sealed class AzureMonitorOptions
{
    /// <summary>
    /// The workspace connection string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Empty disables the exporter, which is the correct default: a service with
    /// no destination configured should start and run, not fail.
    /// </para>
    /// <para>
    /// <b>On whether this is a secret.</b> It contains an instrumentation key,
    /// which is an ingestion identifier rather than a credential — but it is
    /// still not something to publish, because anyone holding it can write junk
    /// into the workspace. It belongs in an environment variable or Key Vault
    /// like the rest of this solution's endpoints; note that <i>reading</i>
    /// telemetry is authorised separately, and this value grants none of it.
    /// </para>
    /// </remarks>
    public string ConnectionString { get; init; } = string.Empty;

    /// <summary>Whether the exporter is wired when a connection string is present.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>True when this exporter should be registered.</summary>
    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(ConnectionString);
}

/// <summary>OTLP export settings.</summary>
/// <remarks>
/// Present so the same build can be pointed at a local collector — Aspire's
/// dashboard, Jaeger, a sidecar — during development without an Azure resource
/// existing. Being able to see spans on a laptop is most of what makes anyone
/// trust them in production.
/// </remarks>
public sealed class OtlpOptions
{
    /// <summary>The collector endpoint. Empty disables the exporter.</summary>
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>Whether the exporter is wired when an endpoint is present.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>True when this exporter should be registered.</summary>
    public bool IsConfigured =>
        Enabled && Uri.TryCreate(Endpoint, UriKind.Absolute, out _);
}
