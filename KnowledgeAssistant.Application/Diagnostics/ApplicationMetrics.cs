using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace KnowledgeAssistant.Application.Diagnostics;

/// <summary>
/// The measurements every use case records, regardless of slice.
/// </summary>
/// <remarks>
/// <para>
/// <b>Built from <see cref="IMeterFactory"/>, not from <c>new Meter(...)</c>.</b>
/// The factory ties the meter's lifetime to the container, which is what lets a
/// test host collect from an isolated meter instead of from a process-wide
/// static that every parallel test would share. It is also the arrangement the
/// runtime's own metrics guidance prescribes.
/// </para>
/// <para>
/// <b>Duration is a histogram, not a counter of totals.</b> An average latency
/// hides exactly the thing worth paging on: a p99 that has doubled while the
/// mean has not moved. Recording the distribution is what makes percentile
/// queries possible at all — they cannot be reconstructed from a sum afterwards.
/// </para>
/// <para>
/// <b>Units follow the semantic conventions</b> (seconds, not milliseconds) so
/// these instruments render on the same axis as the <c>http.server.request.duration</c>
/// the ASP.NET Core instrumentation emits, and so a backend that auto-formats by
/// unit does the right thing.
/// </para>
/// </remarks>
public sealed class ApplicationMetrics
{
    private readonly Counter<long> _requestCount;
    private readonly Histogram<double> _requestDuration;

    /// <summary>Creates the instruments on the host's meter.</summary>
    public ApplicationMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        Meter meter = meterFactory.Create(ApplicationDiagnostics.MeterName);

        _requestCount = meter.CreateCounter<long>(
            "knowledgeassistant.request.count",
            unit: "{request}",
            description: "Use-case requests handled, by request name and outcome.");

        _requestDuration = meter.CreateHistogram<double>(
            "knowledgeassistant.request.duration",
            unit: "s",
            description: "Time to handle a use-case request, from mediator entry to result.");
    }

    /// <summary>
    /// Records one completed request.
    /// </summary>
    /// <remarks>
    /// Both instruments carry the same tags so a rate and a latency chart can be
    /// filtered identically. The error tags are attached only on failure: an
    /// always-present tag whose value is usually empty costs storage on every
    /// series and groups badly.
    /// </remarks>
    public void RecordRequest(
        string requestName,
        string requestKind,
        string outcome,
        double elapsedSeconds,
        string? errorCode,
        string? errorType)
    {
        var tags = new TagList
        {
            { TelemetryTags.RequestName, requestName },
            { TelemetryTags.RequestKind, requestKind },
            { TelemetryTags.Outcome, outcome },
        };

        if (errorCode is not null)
        {
            tags.Add(TelemetryTags.ErrorCode, errorCode);
            tags.Add(TelemetryTags.ErrorType, errorType);
        }

        _requestCount.Add(1, tags);
        _requestDuration.Record(elapsedSeconds, tags);
    }
}
