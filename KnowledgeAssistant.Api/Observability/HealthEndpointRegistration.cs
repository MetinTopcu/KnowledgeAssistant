using System.Text.Json;
using KnowledgeAssistant.Infrastructure.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace KnowledgeAssistant.Api.Observability;

/// <summary>
/// Registers and maps the three health endpoints.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three endpoints because they answer three different questions</b>, and
/// collapsing them is one of the most expensive mistakes available in a
/// containerised deployment:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <c>/health/live</c> — "is this process working?" Contacts nothing. A failure
/// here means the container should be <b>killed and replaced</b>.
/// </description>
/// </item>
/// <item>
/// <description>
/// <c>/health/ready</c> — "can this instance serve traffic right now?" Checks the
/// dependencies. A failure means <b>stop sending it requests</b>, and nothing
/// more.
/// </description>
/// </item>
/// <item>
/// <description>
/// <c>/health</c> — everything, for a human or a monitoring system that wants the
/// whole picture in one call.
/// </description>
/// </item>
/// </list>
/// <para>
/// Point a liveness probe at a dependency check and the failure mode is severe:
/// Storage has a bad minute, every liveness probe fails at once, and the
/// orchestrator restarts the entire fleet — converting a partial dependency
/// outage into a total service outage, and discarding in-flight work from
/// processes that were never unhealthy. The tags exist to keep that from being
/// possible.
/// </para>
/// </remarks>
internal static class HealthEndpointRegistration
{
    /// <summary>The prefix every health endpoint shares.</summary>
    /// <remarks>
    /// Public within the assembly because the tracing filter excludes this path;
    /// two independent copies of the string would drift and quietly re-admit probe
    /// spans.
    /// </remarks>
    public const string BasePath = "/health";

    /// <summary>The tag marking a check as a liveness signal.</summary>
    private const string LivenessTag = "live";

    /// <summary>Registers the liveness check.</summary>
    /// <remarks>
    /// Readiness checks come from <c>AddInfrastructureHealthChecks</c>, because
    /// they concern that layer's dependencies. Liveness belongs here: it is a
    /// statement about the host process, and it deliberately verifies nothing
    /// beyond the fact that the HTTP pipeline reached this delegate — which, for a
    /// probe whose failure means "destroy this container", is the whole of what
    /// can be asserted safely.
    /// </remarks>
    public static IServiceCollection AddHealthEndpoints(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services
            .AddHealthChecks()
            .AddCheck(
                "self",
                () => HealthCheckResult.Healthy("The process is running and serving requests."),
                tags: [LivenessTag]);

        return services;
    }

    /// <summary>Maps <c>/health</c>, <c>/health/live</c>, and <c>/health/ready</c>.</summary>
    public static WebApplication MapHealthEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        HealthProbeOptions probeOptions =
            app.Services.GetRequiredService<IOptions<HealthProbeOptions>>().Value;

        app.MapHealthChecks(BasePath, Options(probeOptions, predicate: null));

        app.MapHealthChecks(
            $"{BasePath}/live",
            Options(probeOptions, check => check.Tags.Contains(LivenessTag)));

        app.MapHealthChecks(
            $"{BasePath}/ready",
            Options(probeOptions, check => check.Tags.Contains(InfrastructureHealthCheckExtensions.ReadinessTag)));

        return app;
    }

    private static HealthCheckOptions Options(
        HealthProbeOptions probeOptions,
        Func<HealthCheckRegistration, bool>? predicate)
    {
        var options = new HealthCheckOptions
        {
            ResponseWriter = (context, report) => WriteResponseAsync(context, report, probeOptions),
        };

        if (predicate is not null)
        {
            options.Predicate = predicate;
        }

        return options;
    }

    /// <summary>
    /// Writes the report as JSON.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The status code carries the answer — 200 for healthy or degraded, 503 for
    /// unhealthy — because that is the only part an orchestrator reads. The body
    /// exists for the human who curls it during an incident.
    /// </para>
    /// <para>
    /// <b>Detail is opt-in.</b> By default the body is the overall status and
    /// nothing else. A detailed body enumerates the service's dependencies and
    /// which of them is currently broken, to anyone who can reach the endpoint —
    /// and health endpoints are routinely left unauthenticated because probes
    /// cannot authenticate. Exception text is never written at any setting: it is
    /// the most likely place for an endpoint or an internal host name to escape.
    /// </para>
    /// </remarks>
    private static Task WriteResponseAsync(
        HttpContext context,
        HealthReport report,
        HealthProbeOptions probeOptions)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        // A cached health response is a lie with a timestamp on it.
        context.Response.Headers.CacheControl = "no-store, no-cache";

        var payload = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["status"] = report.Status.ToString(),
            ["totalDurationMs"] = Math.Round(report.TotalDuration.TotalMilliseconds, 1),
        };

        if (probeOptions.ExposeDetails)
        {
            payload["entries"] = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => (object)new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["status"] = entry.Value.Status.ToString(),
                    ["durationMs"] = Math.Round(entry.Value.Duration.TotalMilliseconds, 1),
                    ["description"] = entry.Value.Description,
                },
                StringComparer.Ordinal);
        }

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
