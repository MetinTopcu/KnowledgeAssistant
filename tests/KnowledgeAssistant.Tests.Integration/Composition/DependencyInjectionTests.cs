using KnowledgeAssistant.Api.Extensions;
using KnowledgeAssistant.Application.Interfaces;
using KnowledgeAssistant.Infrastructure.Azure.Agents;
using KnowledgeAssistant.Infrastructure.Azure.Blob;
using KnowledgeAssistant.Infrastructure.Azure.DocumentIntelligence;
using KnowledgeAssistant.Infrastructure.Azure.OpenAI;
using KnowledgeAssistant.Infrastructure.Search;
using KnowledgeAssistant.Infrastructure.Search.Chunking;
using KnowledgeAssistant.Tests.Integration.Harness;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KnowledgeAssistant.Tests.Integration.Composition;

/// <summary>
/// The real container, composed exactly as production composes it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing is substituted in this file.</b> That is its entire purpose. Every
/// other integration test replaces the Azure adapters, which means none of them
/// would notice if a real registration were deleted, given the wrong lifetime, or
/// left with a dependency the container cannot satisfy — the suite would stay
/// green and the application would fail to start on deploy.
/// </para>
/// <para>
/// Resolving an adapter constructs its SDK client. It does not authenticate or
/// call anything, so these run offline against endpoints that do not resolve.
/// </para>
/// </remarks>
public sealed class DependencyInjectionTests
{
    [Theory]
    [InlineData(typeof(IBlobStorageService))]
    [InlineData(typeof(IDocumentChunkingService))]
    [InlineData(typeof(IEmbeddingService))]
    [InlineData(typeof(IChatService))]
    [InlineData(typeof(IAzureSearchService))]
    [InlineData(typeof(IVectorIndexService))]
    [InlineData(typeof(IAgentService))]
    [InlineData(typeof(ISender))]
    public void EveryPort_ResolvesFromTheRealContainer(Type portType)
    {
        using var factory = new KnowledgeAssistantApiFactory(substituteAzureServices: false);

        using IServiceScope scope = factory.Services.CreateScope();

        scope.ServiceProvider.GetService(portType).Should().NotBeNull(
            $"{portType.Name} is registered by the composition root and injected by a handler");
    }

    [Fact]
    public void AzureAdapters_AreRegisteredAsSingletons()
    {
        // Not a style preference. Both the blob adapter's container-existence cache
        // and the vector adapter's index-existence cache are per-instance, so a
        // scoped or transient lifetime would silently reissue a provisioning check
        // on every request and make the cache pointless.
        using var factory = new KnowledgeAssistantApiFactory(substituteAzureServices: false);

        using IServiceScope first = factory.Services.CreateScope();
        using IServiceScope second = factory.Services.CreateScope();

        first.ServiceProvider.GetRequiredService<IBlobStorageService>()
            .Should().BeSameAs(second.ServiceProvider.GetRequiredService<IBlobStorageService>());
        first.ServiceProvider.GetRequiredService<IVectorIndexService>()
            .Should().BeSameAs(second.ServiceProvider.GetRequiredService<IVectorIndexService>());

        // The agent adapter caches the resolved agent version and the responses
        // client built against it. A shorter lifetime would re-list agent versions
        // — and possibly create one — on every question.
        first.ServiceProvider.GetRequiredService<IAgentService>()
            .Should().BeSameAs(second.ServiceProvider.GetRequiredService<IAgentService>());
    }

    [Fact]
    public void WhenNoDocumentIntelligenceEndpointIsConfigured_TheLocalExtractorIsSelected()
    {
        // The branch in AddDocumentChunking, asserted directly. It is decided once
        // at registration rather than per call, so the container is the only place
        // the choice is observable — and getting it wrong means every scanned
        // document silently yields no text.
        using var factory = new KnowledgeAssistantApiFactory(substituteAzureServices: false);

        factory.Services.GetRequiredService<IPdfTextExtractor>()
            .Should().BeOfType<PdfPigTextExtractor>();
    }

    [Fact]
    public void EveryOptionsSection_IsBoundFromConfiguration()
    {
        // Binding is what ValidateOnStart validates; a section that silently failed
        // to bind would pass validation on its defaults and misconfigure production.
        using var factory = new KnowledgeAssistantApiFactory(substituteAzureServices: false);

        factory.Services.GetRequiredService<IOptions<BlobStorageOptions>>().Value
            .ServiceUri.Should().Be("https://fake.blob.core.windows.net/");
        factory.Services.GetRequiredService<IOptions<AzureSearchOptions>>().Value
            .IndexName.Should().Be("knowledge-index");
        factory.Services.GetRequiredService<IOptions<AzureOpenAIOptions>>().Value
            .ChatDeploymentName.Should().Be("gpt-4o-mini");
        factory.Services.GetRequiredService<IOptions<ChunkingOptions>>().Value
            .MaxChunkSize.Should().Be(800);
        factory.Services.GetRequiredService<IOptions<DocumentIntelligenceOptions>>().Value
            .IsConfigured.Should().BeFalse();

        // The agent section: the two required values bind from the test settings,
        // everything else from committed defaults.
        FoundryAgentOptions agent = factory.Services.GetRequiredService<IOptions<FoundryAgentOptions>>().Value;

        agent.ProjectEndpoint.Should().Be("https://fake.services.ai.azure.com/api/projects/fake-project");
        agent.Version.Should().Be("1");
        agent.Name.Should().Be("knowledge-assistant");
        agent.MaxToolIterations.Should().Be(4);
        agent.Temperature.Should().BeNull("an unset temperature is left to the model");
    }

    [Fact]
    public void WhenARequiredSettingIsMissing_TheHostRefusesToStart()
    {
        // The contract ValidateOnStart exists to provide: a missing endpoint fails
        // the deployment visibly, rather than surfacing as a NullReferenceException
        // on the first upload that reaches storage.
        //
        // The committed appsettings.json ships empty endpoints on purpose, so
        // simply not supplying the test settings reproduces a misconfigured deploy.
        using var factory = new WebApplicationFactory<Program>();

        using WebApplicationFactory<Program> unconfigured =
            factory.WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));

        // Every misconfigured section reports at once, so what surfaces is an
        // AggregateException over several OptionsValidationExceptions rather than
        // one. Unwrapping is the point of the test as much as the throwing is:
        // a deploy should be told all of what is missing, not the first item.
        Exception? thrown = Record.Exception(() => unconfigured.CreateClient());

        thrown.Should().NotBeNull("a host with no endpoints configured must not start");

        IEnumerable<Exception> failures = thrown is AggregateException aggregate
            ? aggregate.Flatten().InnerExceptions
            : [thrown!];

        failures.OfType<OptionsValidationException>()
            .SelectMany(failure => failure.Failures)
            .Should().NotBeEmpty("the failure must name the settings that are missing");
    }

    [Theory]
    [InlineData("Azure:AiFoundry:Agent:ProjectEndpoint", "", "ProjectEndpoint must be configured")]
    [InlineData("Azure:AiFoundry:Agent:ProjectEndpoint", "https://fake.services.ai.azure.com/", "the account endpoint does not serve agents")]
    [InlineData("Azure:AiFoundry:Agent:Version", "", "Version must be set outside Development")]
    [InlineData("Azure:AiFoundry:EmbeddingDimensions", "1536", "produces 3072-dimensional vectors")]
    public void AnInvalidAgentOrEmbeddingSetting_StopsTheHostNamingIt(string key, string value, string expected)
    {
        // Each of these used to surface only on the first agent question or the
        // first upload — as a 404, an unpinned agent, or a dimension mismatch
        // after a blob was already written. Now the host does not start.
        using var factory = new KnowledgeAssistantApiFactory(substituteAzureServices: false);
        factory.Overrides[key] = value;

        Exception? thrown = Record.Exception(() => factory.CreateClient());

        thrown.Should().NotBeNull();

        IEnumerable<Exception> failures = thrown is AggregateException aggregate
            ? aggregate.Flatten().InnerExceptions
            : [thrown!];

        failures.OfType<OptionsValidationException>()
            .SelectMany(failure => failure.Failures)
            .Should().Contain(message => message.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void ApiServices_RegisterTheSharedErrorConventions()
    {
        // AddApiServices is what routes model-binding failures through the same
        // ProblemDetails builder as the Result pipeline. Its absence is invisible
        // until a client receives two error shapes from one endpoint.
        var services = new ServiceCollection();

        services.AddApiServices();

        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IConfigureOptions<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>));
    }
}
