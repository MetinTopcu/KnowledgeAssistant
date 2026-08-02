using KnowledgeAssistant.Infrastructure.Diagnostics;
using KnowledgeAssistant.Tests.Unit.Fakes;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KnowledgeAssistant.Tests.Unit.Diagnostics;

/// <summary>
/// The behaviour every dependency health check inherits: it can be switched off,
/// it is bounded in time, and its result is reused.
/// </summary>
/// <remarks>
/// Tested here rather than through the endpoints because these three properties
/// are about what the check does to the <i>dependency</i> — how often it is
/// called, and for how long it is allowed to hang — which an HTTP-level test
/// cannot see.
/// </remarks>
public sealed class DependencyHealthCheckTests
{
    /// <summary>A check whose probe a test scripts.</summary>
    private sealed class ScriptedHealthCheck(
        MutableOptionsMonitor<HealthProbeOptions> options,
        TimeProvider timeProvider)
        : DependencyHealthCheck(options, timeProvider)
    {
        public int ProbeCount { get; private set; }

        public Exception? Throws { get; set; }

        public TimeSpan Delay { get; set; }

        protected override string DependencyName => "Scripted dependency";

        protected override async Task ProbeAsync(CancellationToken cancellationToken)
        {
            ProbeCount++;

            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }

            if (Throws is not null)
            {
                throw Throws;
            }
        }
    }

    private static readonly DateTimeOffset Start = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    private static (ScriptedHealthCheck Check, MutableOptionsMonitor<HealthProbeOptions> Options, MutableTimeProvider Clock)
        Create(HealthProbeOptions? options = null)
    {
        var monitor = new MutableOptionsMonitor<HealthProbeOptions>(
            options ?? new HealthProbeOptions { CacheSeconds = 0 });
        var clock = new MutableTimeProvider(Start);

        return (new ScriptedHealthCheck(monitor, clock), monitor, clock);
    }

    private static HealthCheckContext Context() => new()
    {
        Registration = new HealthCheckRegistration("scripted", _ => null!, HealthStatus.Unhealthy, tags: null),
    };

    [Fact]
    public async Task WhenDependencyChecksAreDisabled_ReportsHealthyWithoutProbing()
    {
        // Healthy, not Degraded: the operator turned this off deliberately, and a
        // permanent degradation would train everyone to ignore the one signal that
        // is supposed to mean something.
        (ScriptedHealthCheck check, _, _) = Create(new HealthProbeOptions { EnableDependencyChecks = false });

        HealthCheckResult result = await check.CheckHealthAsync(Context(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("disabled by configuration");
        check.ProbeCount.Should().Be(0, "the point of the switch is not to contact the dependency");
    }

    [Fact]
    public async Task WhenTheProbeSucceeds_ReportsHealthy()
    {
        (ScriptedHealthCheck check, _, _) = Create();

        HealthCheckResult result = await check.CheckHealthAsync(Context(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Healthy);
        check.ProbeCount.Should().Be(1);
    }

    [Fact]
    public async Task WhenTheProbeThrows_ReportsUnhealthyWithoutPropagating()
    {
        // An escaping exception would be reported as unhealthy anyway — with the
        // exception text as the description, which is how an endpoint or an
        // internal host name ends up in a response body.
        (ScriptedHealthCheck check, _, _) = Create();
        check.Throws = new InvalidOperationException("https://internal-host.example/secret-path failed");

        HealthCheckResult result = await check.CheckHealthAsync(Context(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().NotContain("internal-host");
        result.Description.Should().Contain("Scripted dependency");
    }

    [Fact]
    public async Task WhenTheProbeExceedsTheTimeout_ReportsUnhealthyRatherThanHanging()
    {
        // A readiness probe that hangs is worse than one that fails: the
        // orchestrator waits on it and the instance is neither serving nor
        // restarted.
        (ScriptedHealthCheck check, _, _) = Create(
            new HealthProbeOptions { TimeoutSeconds = 1, CacheSeconds = 0 });

        check.Delay = TimeSpan.FromSeconds(30);

        HealthCheckResult result = await check.CheckHealthAsync(Context(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("did not respond within 1s");
    }

    [Fact]
    public async Task WhenTheCallerCancels_TheCancellationPropagates()
    {
        // Distinct from the timeout. A probe the caller abandoned says nothing
        // about the dependency, and reporting it as unhealthy would be a lie that
        // takes an instance out of rotation.
        (ScriptedHealthCheck check, _, _) = Create();
        check.Delay = TimeSpan.FromSeconds(30);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        Func<Task> act = () => check.CheckHealthAsync(Context(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task WithinTheCacheWindow_TheDependencyIsContactedOnce()
    {
        // The setting that keeps health checking from becoming its own incident:
        // uncached, every orchestrator poll on every instance is a billed request
        // counting toward the same throttling limits as real traffic.
        (ScriptedHealthCheck check, _, _) = Create(new HealthProbeOptions { CacheSeconds = 10 });

        for (int probe = 0; probe < 5; probe++)
        {
            await check.CheckHealthAsync(Context(), CancellationToken.None);
        }

        check.ProbeCount.Should().Be(1);
    }

    [Fact]
    public async Task AfterTheCacheWindowExpires_TheDependencyIsContactedAgain()
    {
        (ScriptedHealthCheck check, _, MutableTimeProvider clock) =
            Create(new HealthProbeOptions { CacheSeconds = 10 });

        await check.CheckHealthAsync(Context(), CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(11));
        await check.CheckHealthAsync(Context(), CancellationToken.None);

        check.ProbeCount.Should().Be(2);
    }

    [Fact]
    public async Task AFailureIsCachedToo()
    {
        // Deliberate. Retrying a dependency that is already failing, on every
        // probe from every instance, is what turns a degradation into an outage.
        (ScriptedHealthCheck check, _, _) = Create(new HealthProbeOptions { CacheSeconds = 10 });
        check.Throws = new InvalidOperationException("down");

        HealthCheckResult first = await check.CheckHealthAsync(Context(), CancellationToken.None);
        HealthCheckResult second = await check.CheckHealthAsync(Context(), CancellationToken.None);

        first.Status.Should().Be(HealthStatus.Unhealthy);
        second.Status.Should().Be(HealthStatus.Unhealthy);
        check.ProbeCount.Should().Be(1);
    }

    [Fact]
    public async Task WithCachingDisabled_EveryProbeContactsTheDependency()
    {
        (ScriptedHealthCheck check, _, _) = Create(new HealthProbeOptions { CacheSeconds = 0 });

        await check.CheckHealthAsync(Context(), CancellationToken.None);
        await check.CheckHealthAsync(Context(), CancellationToken.None);

        check.ProbeCount.Should().Be(2);
    }

    [Fact]
    public async Task TheSwitchIsReadOnEveryProbe_SoConfigurationReloadsTakeEffect()
    {
        // IOptionsMonitor rather than IOptions, so an operator can disable a
        // dependency check during an incident without restarting the service.
        (ScriptedHealthCheck check, MutableOptionsMonitor<HealthProbeOptions> options, _) =
            Create(new HealthProbeOptions { CacheSeconds = 0 });

        await check.CheckHealthAsync(Context(), CancellationToken.None);
        check.ProbeCount.Should().Be(1);

        options.CurrentValue = new HealthProbeOptions { EnableDependencyChecks = false };

        HealthCheckResult result = await check.CheckHealthAsync(Context(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Healthy);
        check.ProbeCount.Should().Be(1, "the dependency must not be contacted once the check is switched off");
    }
}
