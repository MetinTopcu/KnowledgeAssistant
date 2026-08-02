using System.Diagnostics;

namespace KnowledgeAssistant.Application.Diagnostics;

/// <summary>
/// The names and the <see cref="ActivitySource"/> this layer emits telemetry
/// through.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why an <see cref="ActivitySource"/> and not an OpenTelemetry type.</b>
/// <see cref="ActivitySource"/> is a BCL type. OpenTelemetry does not define the
/// API that instrumented code calls in .NET; it <i>listens</i> to the one the
/// runtime already ships. So this layer can be fully traced without referencing
/// any telemetry vendor, and the choice of backend — Azure Monitor, OTLP, or
/// nothing at all — stays entirely in the composition root.
/// </para>
/// <para>
/// <b>Why the source is static.</b> An <see cref="ActivitySource"/> is designed
/// to be a long-lived singleton keyed by name; the SDK subscribes to it by that
/// name at startup. Resolving one per request from the container would create
/// sources the exporter was never told to listen to, and their spans would
/// silently vanish. Metrics take the opposite approach — see
/// <see cref="ApplicationMetrics"/> — because instruments must be created from
/// the host's <c>IMeterFactory</c> to be collected.
/// </para>
/// <para>
/// When nothing is listening, <see cref="ActivitySource.StartActivity(string, ActivityKind)"/>
/// returns <see langword="null"/> and costs almost nothing, so instrumented code
/// carries no measurable overhead in a host that exports no telemetry.
/// </para>
/// </remarks>
public static class ApplicationDiagnostics
{
    /// <summary>
    /// The activity source name the host subscribes to.
    /// </summary>
    /// <remarks>
    /// Assembly-qualified by convention, so a future <c>KnowledgeAssistant.*</c>
    /// component gets its own source without a name collision, and a wildcard
    /// subscription still picks both up.
    /// </remarks>
    public const string ActivitySourceName = "KnowledgeAssistant.Application";

    /// <summary>The meter name the host subscribes to.</summary>
    public const string MeterName = "KnowledgeAssistant.Application";

    /// <summary>The source every use-case span is started from.</summary>
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}
