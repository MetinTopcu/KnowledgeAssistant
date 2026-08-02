using System.ComponentModel.DataAnnotations;

namespace KnowledgeAssistant.Infrastructure.Diagnostics;

/// <summary>
/// Settings for the dependency health checks.
/// </summary>
/// <remarks>
/// Named for probes rather than "HealthCheckOptions" on purpose: ASP.NET Core
/// already publishes a type by that name for the endpoint side, and two types
/// with one name across the layers they are configured in is a reliable way to
/// bind the wrong one.
/// </remarks>
public sealed class HealthProbeOptions
{
    /// <summary>The configuration section these bind from.</summary>
    public const string SectionName = "Observability:HealthChecks";

    /// <summary>
    /// Whether readiness actually contacts Azure.
    /// </summary>
    /// <remarks>
    /// Turned off, readiness still reports on everything the process can verify
    /// about itself and simply stops reaching outward. That is what makes the
    /// endpoints testable offline, and it is also the switch to reach for when a
    /// dependency is degraded but the service can still serve cached work — the
    /// alternative being a readiness probe that takes the whole deployment out of
    /// rotation over a dependency it does not strictly need.
    /// </remarks>
    public bool EnableDependencyChecks { get; init; } = true;

    /// <summary>
    /// How long a dependency check may take before it is reported unhealthy.
    /// </summary>
    /// <remarks>
    /// Deliberately short. A readiness probe that hangs is worse than one that
    /// fails: the orchestrator waits on it, the request queue backs up behind a
    /// socket nobody is watching, and the instance is neither serving nor
    /// restarted.
    /// </remarks>
    [Range(1, 60, ErrorMessage = "Observability:HealthChecks:TimeoutSeconds must be between 1 and 60.")]
    public int TimeoutSeconds { get; init; } = 5;

    /// <summary>
    /// How long a dependency check result is reused before the dependency is
    /// contacted again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The setting that keeps health checking from becoming its own incident.
    /// Liveness and readiness are polled every few seconds by every orchestrator,
    /// load balancer, and uptime monitor pointed at the service; multiplied by
    /// instance count, an uncached readiness probe turns into a steady, pointless
    /// request rate against Storage and Search that is billed and counts toward
    /// the same throttling limits real traffic does.
    /// </para>
    /// <para>
    /// Worse, it couples the two: once the dependency starts throttling, the probe
    /// fails, every instance is pulled from rotation at once, and the outage is
    /// now total rather than partial. Caching bounds the probe's own load and
    /// keeps readiness from amplifying the problem it is reporting.
    /// </para>
    /// <para>
    /// Zero disables caching, for a deployment that would rather pay for
    /// immediacy.
    /// </para>
    /// </remarks>
    [Range(0, 300, ErrorMessage = "Observability:HealthChecks:CacheSeconds must be between 0 and 300.")]
    public int CacheSeconds { get; init; } = 10;

    /// <summary>
    /// Whether the response body names each check and its status.
    /// </summary>
    /// <remarks>
    /// Off by default. A health endpoint is usually reachable from wherever the
    /// service is, and a detailed body enumerates the service's dependencies to
    /// anyone who asks — useful reconnaissance, and of no use to the orchestrator,
    /// which reads only the status code. Turn it on for an endpoint that is
    /// restricted to an internal network or behind authentication.
    /// </remarks>
    public bool ExposeDetails { get; init; }

    /// <summary>The configured timeout as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);

    /// <summary>The configured cache window as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan CacheDuration => TimeSpan.FromSeconds(CacheSeconds);
}
