using System.Diagnostics;
using KnowledgeAssistant.Application.Abstractions;
using KnowledgeAssistant.Application.Diagnostics;
using KnowledgeAssistant.Domain.Common;
using MediatR;

namespace KnowledgeAssistant.Application.Behaviors;

/// <summary>
/// Traces and times every request that passes through the mediator.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a behaviour rather than instrumentation inside each handler.</b>
/// Tracing and timing are the definition of a cross-cutting concern: identical
/// for every slice, and worth nothing unless applied to all of them. Written into
/// handlers it would be duplicated per slice, drift between them, and — the real
/// failure — be silently absent from the slice somebody adds next sprint. Here it
/// applies to every request that exists and every request that will exist,
/// without a handler knowing it is observed.
/// </para>
/// <para>
/// <b>Why it reports outcomes without exceptions.</b> This solution's handlers
/// return <see cref="Result"/> rather than throwing for expected failures, so a
/// rate-limited embedding call arrives here as a perfectly ordinary return value.
/// A behaviour that only watched for exceptions would record every one of those
/// as a success and report a flawless service that answers nothing. Reading the
/// result is what makes the failure rate real.
/// </para>
/// <para>
/// <b>Cost when nothing is listening.</b> <see cref="ActivitySource.StartActivity(string, ActivityKind)"/>
/// returns <see langword="null"/> with no allocation when no exporter has
/// subscribed, and the metric instruments compile to a no-op check. A host with
/// telemetry disabled pays a timestamp per request.
/// </para>
/// </remarks>
public sealed class ObservabilityBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private const string SuccessOutcome = "success";
    private const string FailureOutcome = "failure";

    private readonly ApplicationMetrics _metrics;
    private readonly IResponseMetricsRecorder<TResponse>? _sliceMetrics;

    /// <summary>Initialises the behaviour.</summary>
    /// <param name="metrics">The measurements common to every slice.</param>
    /// <param name="sliceMetrics">
    /// The slice's own measurements, when it registered any. Optional by design —
    /// see <see cref="IResponseMetricsRecorder{TResponse}"/>.
    /// </param>
    public ObservabilityBehavior(
        ApplicationMetrics metrics,
        IResponseMetricsRecorder<TResponse>? sliceMetrics = null)
    {
        _metrics = metrics;
        _sliceMetrics = sliceMetrics;
    }

    /// <inheritdoc />
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        string requestName = typeof(TRequest).Name;
        string requestKind = ResolveKind();

        // ActivityKind.Internal: this span describes work inside the process. The
        // ASP.NET Core instrumentation already produced the Server span this one
        // nests under, and labelling this one Server too would make one request
        // look like two to any backend that counts entry points.
        using Activity? activity = ApplicationDiagnostics.ActivitySource.StartActivity(
            requestName,
            ActivityKind.Internal);

        activity?.SetTag(TelemetryTags.RequestName, requestName);
        activity?.SetTag(TelemetryTags.RequestKind, requestKind);

        long startedAt = Stopwatch.GetTimestamp();

        try
        {
            TResponse response = await next().ConfigureAwait(false);

            Record(activity, requestName, requestKind, startedAt, response);

            return response;
        }
        catch (Exception exception)
        {
            // An exception here is a genuine fault — a bug or an infrastructure
            // failure the adapters did not translate — as distinct from the
            // expected failures that arrive as a failed Result. Recording it
            // before rethrowing keeps the span honest without swallowing it.
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.AddException(exception);

            _metrics.RecordRequest(
                requestName,
                requestKind,
                FailureOutcome,
                Stopwatch.GetElapsedTime(startedAt).TotalSeconds,
                errorCode: "Unhandled",
                errorType: exception.GetType().Name);

            throw;
        }
    }

    /// <summary>
    /// Reads the outcome off the response and records it on both the span and the
    /// instruments.
    /// </summary>
    private void Record(
        Activity? activity,
        string requestName,
        string requestKind,
        long startedAt,
        TResponse response)
    {
        double elapsedSeconds = Stopwatch.GetElapsedTime(startedAt).TotalSeconds;

        // Every request in this solution returns a Result, but the behaviour is
        // registered open-generically and must stay correct for one that does not.
        if (response is not Result result)
        {
            activity?.SetTag(TelemetryTags.Outcome, SuccessOutcome);
            _metrics.RecordRequest(requestName, requestKind, SuccessOutcome, elapsedSeconds, null, null);
            return;
        }

        if (result.IsSuccess)
        {
            activity?.SetTag(TelemetryTags.Outcome, SuccessOutcome);
            activity?.SetStatus(ActivityStatusCode.Ok);

            _metrics.RecordRequest(requestName, requestKind, SuccessOutcome, elapsedSeconds, null, null);

            // Slice measurements are recorded only on success: a failure has no
            // response to measure.
            _sliceMetrics?.Record(response);

            return;
        }

        string errorCode = result.Error.Code;
        string errorType = result.Error.Type.ToString();

        activity?.SetTag(TelemetryTags.Outcome, FailureOutcome);
        activity?.SetTag(TelemetryTags.ErrorCode, errorCode);
        activity?.SetTag(TelemetryTags.ErrorType, errorType);

        // The code and classification, never Error.Description. A description can
        // embed a file name or a question, which would both explode tag
        // cardinality and copy user-supplied content into telemetry.
        activity?.SetStatus(ActivityStatusCode.Error, errorCode);

        _metrics.RecordRequest(requestName, requestKind, FailureOutcome, elapsedSeconds, errorCode, errorType);
    }

    /// <summary>Classifies the request as a command or a query.</summary>
    /// <remarks>
    /// Worth a tag of its own because the two have different expectations: a
    /// query that slows down is a user waiting, while a command that slows down is
    /// usually an upstream service degrading. Splitting them keeps one from hiding
    /// the other in an aggregate latency chart.
    /// </remarks>
    private static string ResolveKind() =>
        Array.Exists(
            typeof(TRequest).GetInterfaces(),
            contract => contract.IsGenericType
                     && contract.GetGenericTypeDefinition() == typeof(IQuery<>))
            ? "query"
            : "command";
}
