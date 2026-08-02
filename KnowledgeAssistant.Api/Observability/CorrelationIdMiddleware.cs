using System.Diagnostics;
using KnowledgeAssistant.Application.Diagnostics;
using Serilog.Context;

namespace KnowledgeAssistant.Api.Observability;

/// <summary>
/// Establishes a correlation identifier for the request and makes it visible in
/// logs, on the trace, and to the caller.
/// </summary>
/// <remarks>
/// <para>
/// <b>W3C trace context is the mechanism; this is the bridge to it.</b> ASP.NET
/// Core already parses <c>traceparent</c> and continues the caller's trace, so
/// the trace id is the correlation identifier and no extra header is needed
/// between services that speak it. What this adds is the case that actually shows
/// up in support: a client — a browser, a curl, a partner's job — that sends a
/// <c>X-Correlation-Id</c> of its own, quotes it in a ticket, and expects it to
/// find something.
/// </para>
/// <para>
/// So an inbound header is honoured when present and the trace id stands in when
/// it is not; either way the value is attached to the span, pushed into the log
/// context for every line the request produces, and echoed back in the response
/// so the caller can record what to quote later.
/// </para>
/// <para>
/// <b>The inbound value is treated as hostile.</b> It is copied into logs and
/// into a response header, so it is length-capped and filtered to characters that
/// cannot forge a header or corrupt a log line. A client that sends something
/// unreasonable gets the trace id instead.
/// </para>
/// </remarks>
internal sealed class CorrelationIdMiddleware
{
    /// <summary>The request and response header carrying the identifier.</summary>
    internal const string HeaderName = "X-Correlation-Id";

    /// <summary>The response header carrying this request's W3C trace id.</summary>
    /// <remarks>
    /// Returned alongside the correlation id because they are different things
    /// and both are wanted: the correlation id is what the caller chose to call
    /// this request, and the trace id is what the telemetry backend indexed it
    /// under.
    /// </remarks>
    internal const string TraceHeaderName = "X-Trace-Id";

    /// <summary>The longest inbound identifier accepted.</summary>
    private const int MaxLength = 128;

    private readonly RequestDelegate _next;

    /// <summary>Initialises the middleware.</summary>
    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>Runs the middleware.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Activity? activity = Activity.Current;
        string traceId = activity?.TraceId.ToString() ?? context.TraceIdentifier;
        string correlationId = ResolveCorrelationId(context, traceId);

        activity?.SetTag(TelemetryTags.CorrelationId, correlationId);

        // Set on the response before the pipeline continues. Written afterwards,
        // it would be too late for any response the pipeline has already started
        // flushing — which is exactly the slow or failing request whose identifier
        // someone needs.
        context.Response.Headers[HeaderName] = correlationId;
        context.Response.Headers[TraceHeaderName] = traceId;

        // LogContext feeds Serilog's sinks; the ILogger scope feeds every other
        // provider, including the OpenTelemetry one that ships records to Azure
        // Monitor. Both are needed, because neither sees the other's properties.
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            ILogger<CorrelationIdMiddleware> logger =
                context.RequestServices.GetRequiredService<ILogger<CorrelationIdMiddleware>>();

            using (logger.BeginScope(new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["CorrelationId"] = correlationId,
            }))
            {
                await _next(context).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Takes the caller's identifier when it is usable, and the trace id otherwise.
    /// </summary>
    private static string ResolveCorrelationId(HttpContext context, string traceId)
    {
        if (!context.Request.Headers.TryGetValue(HeaderName, out Microsoft.Extensions.Primitives.StringValues values))
        {
            return traceId;
        }

        string? supplied = values.FirstOrDefault();

        return IsAcceptable(supplied) ? supplied! : traceId;
    }

    /// <summary>
    /// Accepts only what is safe to echo into a header and a log line.
    /// </summary>
    /// <remarks>
    /// Control characters are the concern: a carriage return in a value that is
    /// written to a response header is header injection, and one written to a log
    /// is a forged log entry. Restricting to an unambiguous printable set costs
    /// nothing — every identifier format in real use is a subset of it.
    /// </remarks>
    private static bool IsAcceptable(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= MaxLength
        && value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':');
}
