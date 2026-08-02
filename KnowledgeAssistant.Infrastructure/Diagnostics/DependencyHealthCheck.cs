using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace KnowledgeAssistant.Infrastructure.Diagnostics;

/// <summary>
/// The shared behaviour of every check that contacts an external dependency:
/// it can be switched off, it is bounded in time, and its result is reused for a
/// configured window.
/// </summary>
/// <remarks>
/// <para>
/// Written once here because all three properties are easy to leave out of an
/// individual check and each one has bitten production systems. A check without a
/// timeout hangs the probe; a check without caching turns every orchestrator poll
/// into a billed request against the dependency; a check that cannot be disabled
/// makes the whole deployment hostage to a dependency it may not strictly need.
/// </para>
/// <para>
/// <b>Failures are reported, never thrown.</b> An exception escaping a health
/// check is reported by the framework as unhealthy anyway, but with the exception
/// text as the description — which is how connection strings and internal host
/// names end up in a response body. Catching here means the description is
/// something this code chose to say.
/// </para>
/// </remarks>
internal abstract class DependencyHealthCheck : IHealthCheck
{
    private readonly IOptionsMonitor<HealthProbeOptions> _options;
    private readonly TimeProvider _timeProvider;

    // A single reference swap. Concurrent probes may briefly duplicate one call
    // to the dependency, which is cheaper than the lock that would prevent it —
    // health checks are polled on a timer, not fanned out under load.
    private volatile CachedResult? _cached;

    /// <summary>Initialises the check.</summary>
    protected DependencyHealthCheck(
        IOptionsMonitor<HealthProbeOptions> options,
        TimeProvider timeProvider)
    {
        _options = options;
        _timeProvider = timeProvider;
    }

    /// <summary>A short name for the dependency, used in descriptions.</summary>
    protected abstract string DependencyName { get; }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        HealthProbeOptions options = _options.CurrentValue;

        if (!options.EnableDependencyChecks)
        {
            // Healthy, not Degraded. The operator turned this off deliberately;
            // reporting a permanent degradation would train everyone to ignore the
            // one signal that is supposed to mean something.
            return HealthCheckResult.Healthy($"{DependencyName} checks are disabled by configuration.");
        }

        if (TryUseCachedResult(options, out HealthCheckResult cached))
        {
            return cached;
        }

        HealthCheckResult result = await ProbeWithTimeoutAsync(options, cancellationToken).ConfigureAwait(false);

        _cached = new CachedResult(result, _timeProvider.GetUtcNow());

        return result;
    }

    /// <summary>
    /// Contacts the dependency, throwing if it is unreachable or misconfigured.
    /// </summary>
    /// <remarks>
    /// Implementations should make the cheapest call that proves both reachability
    /// and authorisation. Proving reachability alone is close to worthless here:
    /// the failure this system is actually likely to suffer is a managed identity
    /// missing a role assignment, which answers promptly and refuses.
    /// </remarks>
    protected abstract Task ProbeAsync(CancellationToken cancellationToken);

    private bool TryUseCachedResult(HealthProbeOptions options, out HealthCheckResult result)
    {
        result = default;

        if (options.CacheSeconds == 0)
        {
            return false;
        }

        CachedResult? cached = _cached;

        if (cached is null || _timeProvider.GetUtcNow() - cached.RecordedAt >= options.CacheDuration)
        {
            return false;
        }

        result = cached.Result;
        return true;
    }

    private async Task<HealthCheckResult> ProbeWithTimeoutAsync(
        HealthProbeOptions options,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Timeout);

        try
        {
            await ProbeAsync(timeout.Token).ConfigureAwait(false);

            return HealthCheckResult.Healthy($"{DependencyName} is reachable.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Our timeout fired rather than the caller giving up. Distinguishing
            // the two matters: a probe the caller abandoned says nothing about the
            // dependency, while one that ran out of time says a great deal.
            return HealthCheckResult.Unhealthy(
                $"{DependencyName} did not respond within {options.TimeoutSeconds}s.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy($"{DependencyName} is not reachable.", exception);
        }
    }

    private sealed record CachedResult(HealthCheckResult Result, DateTimeOffset RecordedAt);
}
