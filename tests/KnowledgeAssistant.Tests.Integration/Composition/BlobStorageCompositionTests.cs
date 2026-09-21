using Azure.Storage.Blobs;
using KnowledgeAssistant.Infrastructure.Azure.Blob;
using KnowledgeAssistant.Infrastructure.DependencyInjection;
using KnowledgeAssistant.Tests.Integration.Harness;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace KnowledgeAssistant.Tests.Integration.Composition;

/// <summary>
/// Which blob client the real composition root builds for each configuration.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the container is built by hand here.</b> The Azurite path is legal only
/// in Development, and hosting the API as Development would load the developer's
/// own <c>appsettings.Development.json</c> and user secrets — machine-specific
/// state a test must not depend on. Calling <c>AddInfrastructure</c> directly,
/// with the host environment supplied explicitly, exercises the same registration
/// and the same validator without any of that.
/// </para>
/// <para>
/// <b>How the authentication mode is observed.</b>
/// <see cref="BlobServiceClient.CanGenerateAccountSasUri"/> is true only for a
/// client holding a shared key. A client built on <c>DefaultAzureCredential</c>
/// has none, so the property distinguishes the two paths without a network call.
/// </para>
/// </remarks>
public sealed class BlobStorageCompositionTests
{
    private const string ComposeAzurite =
        "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;" +
        $"AccountKey={BlobStorageOptionsValidator.AzuriteAccountKey};" +
        "BlobEndpoint=http://azurite:10000/devstoreaccount1;";

    private sealed class StubHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "KnowledgeAssistant.Api";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static ServiceProvider Compose(string environment, Dictionary<string, string?> overrides)
    {
        Dictionary<string, string?> settings = KnowledgeAssistantApiFactory.Settings;

        foreach ((string key, string? value) in overrides)
        {
            settings[key] = value;
        }

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        var hostEnvironment = new StubHostEnvironment(environment);
        services.AddSingleton<IHostEnvironment>(hostEnvironment);
        services.AddInfrastructure(configuration, hostEnvironment);

        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public void WithServiceUri_TheClientTargetsTheAccountAndHoldsNoKey(string environment)
    {
        using ServiceProvider provider = Compose(environment, new(StringComparer.Ordinal));

        // Resolving the options runs the validators, as ValidateOnStart does.
        provider.GetRequiredService<IOptions<BlobStorageOptions>>().Value
            .UsesDevelopmentStorage.Should().BeFalse();

        BlobServiceClient client = provider.GetRequiredService<BlobServiceClient>();

        client.Uri.Should().Be(new Uri("https://fake.blob.core.windows.net/"));
        client.CanGenerateAccountSasUri.Should().BeFalse(
            "the production client authenticates with DefaultAzureCredential and must hold no account key");
    }

    [Theory]
    [InlineData("UseDevelopmentStorage=true", "http://127.0.0.1:10000/devstoreaccount1")]
    [InlineData(ComposeAzurite, "http://azurite:10000/devstoreaccount1")]
    public void WithAzuriteConnectionString_InDevelopment_TheClientTargetsAzurite(
        string connectionString,
        string expectedUri)
    {
        using ServiceProvider provider = Compose("Development", new(StringComparer.Ordinal)
        {
            ["Azure:Storage:ServiceUri"] = string.Empty,
            ["Azure:Storage:ConnectionString"] = connectionString,
        });

        provider.GetRequiredService<IOptions<BlobStorageOptions>>().Value
            .UsesDevelopmentStorage.Should().BeTrue();

        BlobServiceClient client = provider.GetRequiredService<BlobServiceClient>();

        client.Uri.Should().Be(new Uri(expectedUri));
        client.AccountName.Should().Be("devstoreaccount1");
        client.CanGenerateAccountSasUri.Should().BeTrue("Azurite is reached with its shared key");
    }

    [Fact]
    public void WithAzuriteConnectionString_OutsideDevelopment_TheRealHostRefusesToStart()
    {
        // The real host this time, in its Testing environment, because the claim
        // is about startup: a deployed process given a connection string must
        // fail before it serves anything, not on the first upload.
        using var factory = new KnowledgeAssistantApiFactory(substituteAzureServices: false);

        factory.Overrides["Azure:Storage:ServiceUri"] = string.Empty;
        factory.Overrides["Azure:Storage:ConnectionString"] = "UseDevelopmentStorage=true";

        Exception? thrown = Record.Exception(() => factory.CreateClient());

        thrown.Should().NotBeNull();

        IEnumerable<Exception> failures = thrown is AggregateException aggregate
            ? aggregate.Flatten().InnerExceptions
            : [thrown!];

        failures.OfType<OptionsValidationException>()
            .SelectMany(failure => failure.Failures)
            .Should().Contain(message => message.Contains("only permitted in the Development environment"));
    }
}
