using Azure;
using Azure.Identity;
using KnowledgeAssistant.Infrastructure.Diagnostics;
using KnowledgeAssistant.Tests.Unit.Fakes;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KnowledgeAssistant.Tests.Unit.Diagnostics;

/// <summary>
/// The readiness check for Azure AI Search: what it asks the service, and what it
/// reports for each answer.
/// </summary>
/// <remarks>
/// <para>
/// <b>It asks about the service, never about an index.</b> Both indexes are created
/// on first use, so a freshly deployed instance legitimately has neither. Service
/// statistics require an authorised identity and a reachable endpoint and nothing
/// else, which is exactly what readiness can honestly claim.
/// </para>
/// <para>
/// Caching, timeout and the enable switch are inherited and covered by
/// <see cref="DependencyHealthCheckTests"/>. Caching is off here so every call
/// probes.
/// </para>
/// </remarks>
public sealed class AzureSearchHealthCheckTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private static (AzureSearchHealthCheck Check, FakeSearchIndexClient IndexClient) Create()
    {
        var indexClient = new FakeSearchIndexClient(new FakeSearchClient());

        var check = new AzureSearchHealthCheck(
            indexClient,
            new MutableOptionsMonitor<HealthProbeOptions>(new HealthProbeOptions { CacheSeconds = 0 }),
            new MutableTimeProvider(Start));

        return (check, indexClient);
    }

    private static Task<HealthCheckResult> Run(AzureSearchHealthCheck check) =>
        check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

    [Fact]
    public async Task WhenServiceStatisticsAreReturned_ReportsHealthy()
    {
        (AzureSearchHealthCheck check, FakeSearchIndexClient indexClient) = Create();

        HealthCheckResult result = await Run(check);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Be("Azure AI Search is reachable.");
        indexClient.StatisticsCallCount.Should().Be(1);
    }

    [Fact]
    public async Task ProbesTheServiceStatisticsEndpointOnly_AndNeverAnIndex()
    {
        // A check that demanded an index would report a correctly deployed service
        // as unready until somebody uploaded a document. And a probe must never
        // create what it inspects.
        (AzureSearchHealthCheck check, FakeSearchIndexClient indexClient) = Create();

        await Run(check);

        indexClient.StatisticsCallCount.Should().Be(1);
        indexClient.GetCallCount.Should().Be(0);
        indexClient.CreateCallCount.Should().Be(0);
        indexClient.RequestedClientNames.Should().BeEmpty("no index-level client is needed to prove the service answers");
    }

    [Theory]
    [InlineData(403, "the identity is authenticated but lacks a role on the service")]
    [InlineData(401, "the token was rejected")]
    [InlineData(503, "the service is unavailable")]
    public async Task WhenTheServiceRefusesTheStatisticsRequest_ReportsUnhealthy(int status, string because)
    {
        // The case this probe is chosen for: a missing role assignment answers
        // promptly and refuses. A reachability-only check would call that healthy.
        (AzureSearchHealthCheck check, FakeSearchIndexClient indexClient) = Create();
        var refusal = new RequestFailedException(status, "Forbidden: identity 00000000-0000 has no role on srch-internal-name");
        indexClient.ThrowNextStatistics = refusal;

        HealthCheckResult result = await Run(check);

        result.Status.Should().Be(HealthStatus.Unhealthy, because);
        result.Exception.Should().BeSameAs(refusal, "the full exception is kept for logging");
    }

    [Fact]
    public async Task WhenNoCredentialCanBeObtained_ReportsUnhealthy()
    {
        (AzureSearchHealthCheck check, FakeSearchIndexClient indexClient) = Create();
        var failure = new AuthenticationFailedException("DefaultAzureCredential failed to retrieve a token");
        indexClient.ThrowNextStatistics = failure;

        HealthCheckResult result = await Run(check);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().BeSameAs(failure);
    }

    [Fact]
    public async Task WhenRetriesAreExhaustedAtTheTransportLevel_ReportsUnhealthy()
    {
        (AzureSearchHealthCheck check, FakeSearchIndexClient indexClient) = Create();
        indexClient.ThrowNextStatistics = new AggregateException(new HttpRequestException("name resolution failed"));

        HealthCheckResult result = await Run(check);

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task AFailureDescription_NamesTheDependencyWithoutRepeatingTheServicesMessage()
    {
        // The description can reach an HTTP response body; the exception text names
        // identities and internal resource names, and must stay in the log.
        (AzureSearchHealthCheck check, FakeSearchIndexClient indexClient) = Create();
        indexClient.ThrowNextStatistics =
            new RequestFailedException(403, "Forbidden: identity 00000000-0000 has no role on srch-internal-name");

        HealthCheckResult result = await Run(check);

        result.Description.Should().Be("Azure AI Search is not reachable.");
        result.Description.Should().NotContain("srch-internal-name");
    }
}
