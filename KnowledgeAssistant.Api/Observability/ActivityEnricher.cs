using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace KnowledgeAssistant.Api.Observability;

/// <summary>
/// Adds the current trace and span identifiers to every Serilog event.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this rather than a package.</b> Serilog enrichers for tracing exist as
/// third-party packages, and all of them do what these twenty lines do: read
/// <see cref="Activity.Current"/>. Taking a dependency to avoid writing this
/// would add a supply-chain surface and a version to track for no capability.
/// </para>
/// <para>
/// <b>Why it matters.</b> Serilog's own sinks — the console, and whatever a
/// deployment adds — are outside OpenTelemetry, so records written there carry no
/// trace context unless something puts it in the message. Without these
/// properties, a console log line from a failing request cannot be joined to the
/// trace that explains it, which is precisely the join anyone debugging wants to
/// make first.
/// </para>
/// <para>
/// It enriches events written outside a request too — startup, background work —
/// where there is often no activity at all, in which case nothing is added rather
/// than an empty property that would clutter every line.
/// </para>
/// </remarks>
internal sealed class ActivityEnricher : ILogEventEnricher
{
    /// <inheritdoc />
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(propertyFactory);

        Activity? activity = Activity.Current;

        if (activity is null)
        {
            return;
        }

        logEvent.AddPropertyIfAbsent(
            propertyFactory.CreateProperty("TraceId", activity.TraceId.ToString()));

        logEvent.AddPropertyIfAbsent(
            propertyFactory.CreateProperty("SpanId", activity.SpanId.ToString()));
    }
}
