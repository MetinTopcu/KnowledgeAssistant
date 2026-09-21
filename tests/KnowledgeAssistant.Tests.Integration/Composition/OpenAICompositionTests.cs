using System.Reflection;
using KnowledgeAssistant.Infrastructure.DependencyInjection;
using KnowledgeAssistant.Tests.Integration.Harness;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using OpenAI.Chat;
using OpenAI.Embeddings;

// Endpoint and Model are marked evaluation-only (OPENAI001). They are read here
// only to observe wiring; no production code depends on them.
#pragma warning disable OPENAI001

namespace KnowledgeAssistant.Tests.Integration.Composition;

/// <summary>
/// Where the real composition root points the chat and embedding clients.
/// </summary>
/// <remarks>
/// <para>
/// These clients are the OpenAI library aimed at the Foundry resource's
/// <c>/openai/v1/</c> endpoint. <c>Azure.AI.OpenAI</c> 2.1.0 was used before and
/// built cleanly against the <c>OpenAI</c> 2.9.1 the agent packages require, but
/// failed every chat call at run time with <c>MissingMethodException</c>. Every
/// unit test passed, because they fake the client; only a live call showed it.
/// These tests pin the replacement's wiring without a network call.
/// </para>
/// </remarks>
public sealed class OpenAICompositionTests
{
    private sealed class StubHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "KnowledgeAssistant.Api";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static ServiceProvider Compose(string foundryEndpoint)
    {
        Dictionary<string, string?> settings = KnowledgeAssistantApiFactory.Settings;
        settings["Azure:AiFoundry:Endpoint"] = foundryEndpoint;

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var hostEnvironment = new StubHostEnvironment("Production");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(hostEnvironment);
        services.AddInfrastructure(configuration, hostEnvironment);

        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData("https://fake.services.ai.azure.com/")]
    [InlineData("https://fake.services.ai.azure.com")]
    public void ChatAndEmbeddingClients_TargetTheV1EndpointOfTheResource(string foundryEndpoint)
    {
        using ServiceProvider provider = Compose(foundryEndpoint);

        var expected = new Uri("https://fake.services.ai.azure.com/openai/v1/");

        provider.GetRequiredService<ChatClient>().Endpoint.Should().Be(expected);
        provider.GetRequiredService<EmbeddingClient>().Endpoint.Should().Be(expected);
    }

    [Fact]
    public void ChatAndEmbeddingClients_AddressTheConfiguredDeploymentsAsModels()
    {
        using ServiceProvider provider = Compose("https://fake.services.ai.azure.com/");

        provider.GetRequiredService<ChatClient>().Model.Should().Be("gpt-4o-mini");
        provider.GetRequiredService<EmbeddingClient>().Model.Should().Be("text-embedding-3-small");
    }

    [Fact]
    public void Infrastructure_DoesNotReferenceAzureAIOpenAI()
    {
        AssemblyName[] references = typeof(InfrastructureServiceRegistration).Assembly.GetReferencedAssemblies();

        references.Should().NotContain(
            reference => reference.Name == "Azure.AI.OpenAI",
            "Azure.AI.OpenAI 2.1.0 is binary-incompatible with the OpenAI 2.9.1 this graph resolves");
    }
}
