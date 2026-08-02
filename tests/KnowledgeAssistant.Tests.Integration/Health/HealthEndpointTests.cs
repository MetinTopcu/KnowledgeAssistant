using System.Net;
using System.Text.Json;
using KnowledgeAssistant.Tests.Integration.Harness;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAssistant.Tests.Integration.Health;

/// <summary>
/// The three health endpoints, driven over HTTP.
/// </summary>
/// <remarks>
/// <para>
/// The load-bearing test in this file is
/// <see cref="Liveness_StaysHealthyWhenAReadinessCheckFails"/>. Everything else
/// here confirms plumbing; that one confirms the property the whole tag
/// arrangement exists to guarantee — that a dependency outage takes instances out
/// of rotation without the orchestrator killing them. Get it wrong and the
/// failure only appears in production, at the worst moment, as the entire fleet
/// restarting in unison because Storage had a bad minute.
/// </para>
/// </remarks>
public sealed class HealthEndpointTests
{
    private static readonly Uri Health = new("/health", UriKind.Relative);
    private static readonly Uri Live = new("/health/live", UriKind.Relative);
    private static readonly Uri Ready = new("/health/ready", UriKind.Relative);

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task EveryHealthEndpoint_IsMappedAndReportsHealthy(string path)
    {
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        body.RootElement.GetProperty("status").GetString().Should().Be("Healthy");
        body.RootElement.GetProperty("totalDurationMs").GetDouble().Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task HealthEndpoints_AreNotCacheable()
    {
        // A cached health response is a lie with a timestamp on it: the proxy
        // keeps answering "healthy" for an instance that stopped being so.
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(Ready);

        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Headers.CacheControl.NoCache.Should().BeTrue();
    }

    [Fact]
    public async Task Liveness_RunsOnlyTheSelfCheckAndNeverADependency()
    {
        // The separation that keeps a dependency outage from becoming a restart
        // loop. If a readiness check ever appears in this list, liveness has
        // started depending on something outside the process.
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(Live);

        IEnumerable<string> entries = await EntryNamesAsync(response);

        entries.Should().Equal("self");
    }

    [Fact]
    public async Task Readiness_RunsTheDependencyChecksAndNotTheSelfCheck()
    {
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(Ready);

        IEnumerable<string> entries = await EntryNamesAsync(response);

        entries.Should().BeEquivalentTo("blob-storage", "azure-search");
    }

    [Fact]
    public async Task Health_ReportsEveryCheck()
    {
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(Health);

        IEnumerable<string> entries = await EntryNamesAsync(response);

        entries.Should().BeEquivalentTo("self", "blob-storage", "azure-search");
    }

    [Fact]
    public async Task WhenDependencyChecksAreDisabled_ReadinessReportsHealthyWithoutContactingAzure()
    {
        // The switch that makes these endpoints testable offline is also the one
        // an operator reaches for to keep a degraded-but-optional dependency from
        // pulling the whole deployment out of rotation.
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(Ready);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        body.RootElement.GetProperty("entries").GetProperty("blob-storage")
            .GetProperty("description").GetString()
            .Should().Contain("disabled by configuration");
    }

    [Fact]
    public async Task WhenDetailsAreNotExposed_TheBodyCarriesOnlyTheOverallStatus()
    {
        // The default. A detailed body enumerates this service's dependencies and
        // which of them is broken, to anyone who can reach an endpoint that probes
        // cannot authenticate to.
        using var factory = new KnowledgeAssistantApiFactory();
        factory.Overrides["Observability:HealthChecks:ExposeDetails"] = "false";
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(Health);

        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        body.RootElement.GetProperty("status").GetString().Should().Be("Healthy");
        body.RootElement.TryGetProperty("entries", out _).Should().BeFalse();
    }

    [Fact]
    public async Task WhenAReadinessCheckFails_ReadinessAnswers503()
    {
        // 503 rather than 500: the orchestrator reads it as "not now", which is
        // what a failed dependency means, and load balancers already treat it as
        // a signal to stop routing here.
        using WebApplicationFactory<Program> factory = FactoryWithFailingReadinessCheck();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(Ready);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        body.RootElement.GetProperty("status").GetString().Should().Be("Unhealthy");
    }

    [Fact]
    public async Task Liveness_StaysHealthyWhenAReadinessCheckFails()
    {
        // The property the entire tag arrangement exists for. A dependency being
        // down must remove the instance from rotation and must NOT make the
        // orchestrator kill it — otherwise one bad minute from Storage restarts
        // every instance at once and turns a partial outage into a total one.
        using WebApplicationFactory<Program> factory = FactoryWithFailingReadinessCheck();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage readiness = await client.GetAsync(Ready);
        using HttpResponseMessage liveness = await client.GetAsync(Live);

        readiness.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        liveness.StatusCode.Should().Be(HttpStatusCode.OK, "the process is fine; its dependency is not");
    }

    [Fact]
    public async Task HealthEndpoints_AnswerBeforeAnyAuthenticationOrContentNegotiation()
    {
        // A probe sends no Accept header and no credentials. An endpoint that
        // requires either is an endpoint the orchestrator reports as down.
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, Live);
        request.Headers.Accept.Clear();

        using HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// A host whose readiness set contains one check that always fails.
    /// </summary>
    /// <remarks>
    /// Scripted rather than provoked by pointing at an unreachable Azure endpoint:
    /// that would make the test depend on DNS behaviour and a timeout, which is
    /// both slow and flaky. What is under test is how the endpoints react to an
    /// unhealthy readiness check, not how a socket fails.
    /// </remarks>
    private static WebApplicationFactory<Program> FactoryWithFailingReadinessCheck()
    {
        var factory = new KnowledgeAssistantApiFactory();

        return factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddHealthChecks().AddCheck(
                    "scripted-dependency",
                    () => HealthCheckResult.Unhealthy("Scripted failure."),
                    tags: ["ready"])));
    }

    private static async Task<IEnumerable<string>> EntryNamesAsync(HttpResponseMessage response)
    {
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return [.. body.RootElement.GetProperty("entries").EnumerateObject().Select(entry => entry.Name)];
    }
}
