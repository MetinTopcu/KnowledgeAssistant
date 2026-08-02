using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KnowledgeAssistant.Infrastructure.Diagnostics;

/// <summary>
/// Registers health checks for the dependencies this layer owns.
/// </summary>
/// <remarks>
/// <para>
/// Here rather than in the API for the same reason <c>AddInfrastructure</c> is:
/// these checks need the SDK clients, and this is the only layer that knows they
/// exist. The API maps the endpoints and decides what an unhealthy result means
/// to a caller; it does not learn what a document container is.
/// </para>
/// <para>
/// Separate from <c>AddInfrastructure</c>, and called explicitly by the host, so
/// that composing the adapters and exposing their health remain independent
/// decisions — a background worker built on this layer wants the former and has
/// no use for the latter.
/// </para>
/// </remarks>
public static class InfrastructureHealthCheckExtensions
{
    /// <summary>
    /// The tag marking a check as a readiness signal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The distinction this tag carries is the one that matters most in a health
    /// setup, and the one most often got wrong: <b>liveness must never test a
    /// dependency</b>. A liveness probe that contacts Storage restarts every
    /// instance when Storage has a bad minute — replacing a partial outage with a
    /// total one, and losing the in-flight work of processes that were perfectly
    /// healthy.
    /// </para>
    /// <para>
    /// Readiness is where dependency checks belong. Failing it removes the
    /// instance from rotation without killing it, so it recovers by itself when
    /// the dependency does.
    /// </para>
    /// </remarks>
    public const string ReadinessTag = "ready";

    /// <summary>Registers the dependency checks and binds their settings.</summary>
    public static IServiceCollection AddInfrastructureHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Same contract as every other options section in this layer: a value out
        // of range fails the deployment at startup rather than at the first probe.
        services
            .AddOptions<HealthProbeOptions>()
            .Bind(configuration.GetSection(HealthProbeOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // The checks read it for cache expiry. Application registers this too;
        // TryAdd means whichever composition root runs first wins and a test host
        // can still substitute a fake clock ahead of both.
        services.TryAddSingleton(TimeProvider.System);

        services.AddHealthChecks()
            .AddCheck<BlobStorageHealthCheck>(
                "blob-storage",
                tags: [ReadinessTag])
            .AddCheck<AzureSearchHealthCheck>(
                "azure-search",
                tags: [ReadinessTag]);

        return services;
    }
}
