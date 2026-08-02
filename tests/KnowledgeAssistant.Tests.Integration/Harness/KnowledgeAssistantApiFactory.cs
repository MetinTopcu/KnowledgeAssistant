using KnowledgeAssistant.Application.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KnowledgeAssistant.Tests.Integration.Harness;

/// <summary>
/// Hosts the real API in memory, with the Azure-backed ports substituted.
/// </summary>
/// <remarks>
/// <para>
/// <b>What stays real.</b> Routing, model binding, filters, the
/// <c>InvalidModelStateResponseFactory</c>, MediatR, FluentValidation, the
/// Result-to-ProblemDetails mapping, and chunking. That list is the point: those
/// are the parts a unit test cannot reach, and every one of them can break in a
/// way that only shows up over HTTP.
/// </para>
/// <para>
/// <b>What is replaced, and where.</b> Only the six ports that would otherwise
/// call Azure, and only through <c>ConfigureTestServices</c> — which runs after
/// <c>Program.cs</c> has registered everything, so the substitution is a genuine
/// replacement of a registration that really happened. Substituting in
/// <c>ConfigureServices</c> instead would run first and be silently overwritten.
/// </para>
/// <para>
/// <b>Configuration is supplied here.</b> The committed <c>appsettings.json</c>
/// deliberately ships empty endpoints, and every options class is registered with
/// <c>ValidateOnStart</c>, so the host refuses to start without them. Filling
/// them in with syntactically valid but unroutable values is what makes that
/// startup contract part of what this harness proves: if a future required
/// setting is added without a default, these tests fail immediately.
/// </para>
/// <para>
/// The environment is <c>Testing</c> rather than <c>Development</c> so the
/// developer-only OpenAPI endpoint is not mapped and
/// <c>appsettings.Development.json</c> — which is machine-specific and not
/// committed — cannot influence a test run.
/// </para>
/// </remarks>
internal sealed class KnowledgeAssistantApiFactory : WebApplicationFactory<Program>
{
    private readonly bool _substituteAzureServices;

    /// <param name="substituteAzureServices">
    /// When false, the container is composed exactly as production composes it.
    /// Used by the composition tests, which exist to prove the real registrations
    /// resolve — the check a fake-everything suite quietly stops performing.
    /// </param>
    public KnowledgeAssistantApiFactory(bool substituteAzureServices = true) =>
        _substituteAzureServices = substituteAzureServices;

    public FakeAzureEnvironment Azure { get; } = new();

    /// <summary>
    /// Settings that satisfy every <c>ValidateOnStart</c> check.
    /// </summary>
    /// <remarks>
    /// The Document Intelligence endpoint is left empty on purpose: that is the
    /// branch which selects the local PdfPig extractor, and it is the branch these
    /// tests need in order to run without a network.
    /// </remarks>
    /// <summary>
    /// Settings applied after <see cref="Settings"/>, for a test that needs a
    /// different configuration.
    /// </summary>
    /// <remarks>
    /// Populate before the first call to <c>CreateClient</c>; the host is built
    /// lazily on that call, and a value added afterwards arrives too late to be
    /// read.
    /// </remarks>
    public Dictionary<string, string?> Overrides { get; } = new(StringComparer.Ordinal);

    public static Dictionary<string, string?> Settings => new(StringComparer.Ordinal)
    {
        // Telemetry collection stays on — the pipeline behaviour, the activity
        // source, and the instrumentation all run, so a test host exercises the
        // same code path production does. Only the exporters are left
        // unconfigured, which is what keeps the suite from trying to reach Azure
        // Monitor.
        ["Observability:AzureMonitor:ConnectionString"] = string.Empty,
        ["Observability:Otlp:Endpoint"] = string.Empty,

        // Readiness does not contact Azure by default. The endpoints, the tag
        // predicates, and the response writer are all still genuinely exercised;
        // what is suppressed is the network call behind them, which no offline
        // suite could make. HealthEndpointTests overrides this where it matters.
        ["Observability:HealthChecks:EnableDependencyChecks"] = "false",
        ["Observability:HealthChecks:ExposeDetails"] = "true",
        ["Azure:Storage:ServiceUri"] = "https://fake.blob.core.windows.net/",
        ["Azure:Storage:DocumentsContainer"] = "documents",
        ["Azure:Search:Endpoint"] = "https://fake.search.windows.net/",
        ["Azure:Search:IndexName"] = "knowledge-index",
        ["Azure:Search:ChunkIndexName"] = "knowledge-chunks",
        ["Azure:DocumentIntelligence:Endpoint"] = string.Empty,
        ["Azure:AiFoundry:Endpoint"] = "https://fake.services.ai.azure.com/",
        ["Azure:AiFoundry:ChatDeploymentName"] = "gpt-4o-mini",
        ["Azure:AiFoundry:EmbeddingDeploymentName"] = "text-embedding-3-small",
        ["Chunking:MaxChunkSize"] = "800",
        ["Chunking:OverlapSize"] = "200",
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // UseSetting, not ConfigureAppConfiguration. Under minimal hosting the
        // entry point reads builder.Configuration while it is still executing —
        // AddInfrastructure resolves the embedding and chat deployment names at
        // registration time, to construct the SDK clients — and
        // ConfigureAppConfiguration callbacks are not applied until Build().
        // Settings supplied this way are part of the host configuration the
        // WebApplicationBuilder starts from, so they are visible early enough.
        foreach ((string key, string? value) in Settings)
        {
            builder.UseSetting(key, value);
        }

        foreach ((string key, string? value) in Overrides)
        {
            builder.UseSetting(key, value);
        }

        if (!_substituteAzureServices)
        {
            return;
        }

        builder.ConfigureTestServices(services =>
        {
            Replace<IBlobStorageService>(services, Azure.Blob);
            Replace<IEmbeddingService>(services, Azure.Embeddings);
            Replace<IVectorIndexService>(services, Azure.VectorIndex);
            Replace<IAzureSearchService>(services, Azure.SearchIndex);
            Replace<IChatService>(services, Azure.Chat);
            Replace<IAgentService>(services, Azure.Agent);
        });
    }

    /// <summary>
    /// Removes every registration for <typeparamref name="TService"/> before
    /// adding the fake.
    /// </summary>
    /// <remarks>
    /// <c>RemoveAll</c> rather than <c>AddSingleton</c> alone. The container
    /// resolves the last registration for a single-service request, so simply
    /// adding would appear to work — until something injects
    /// <c>IEnumerable&lt;TService&gt;</c> and gets the real adapter alongside the
    /// fake.
    /// </remarks>
    private static void Replace<TService>(IServiceCollection services, TService instance)
        where TService : class
    {
        services.RemoveAll<TService>();
        services.AddSingleton(instance);
    }
}
