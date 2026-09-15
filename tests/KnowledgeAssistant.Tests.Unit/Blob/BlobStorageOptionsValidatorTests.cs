using KnowledgeAssistant.Infrastructure.Azure.Blob;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace KnowledgeAssistant.Tests.Unit.Blob;

/// <summary>
/// Which blob configurations a host may start with.
/// </summary>
/// <remarks>
/// <para>
/// Two things are being protected, and they pull in opposite directions. The
/// production rule must not have moved at all: a missing or relative
/// <c>ServiceUri</c> still fails with the message it always did. And the new
/// emulator path must be impossible to use as a way of running a deployed host
/// on an account key.
/// </para>
/// <para>
/// The connection strings below match what <c>Azure.Storage.Blobs</c> 12.29.1
/// was observed to accept, so a rule here that is stricter or looser than the
/// SDK shows up as a failing case rather than a surprise at startup.
/// </para>
/// </remarks>
public sealed class BlobStorageOptionsValidatorTests
{
    private const string AzuriteKey = BlobStorageOptionsValidator.AzuriteAccountKey;

    private const string LoopbackAzurite =
        $"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey={AzuriteKey};BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;";

    private const string ComposeAzurite =
        $"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey={AzuriteKey};BlobEndpoint=http://azurite:10000/devstoreaccount1;";

    private sealed class StubHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "KnowledgeAssistant.Api";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static ValidateOptionsResult Validate(
        string environment,
        string serviceUri = "",
        string connectionString = "") =>
        new BlobStorageOptionsValidator(new StubHostEnvironment(environment)).Validate(
            Options.DefaultName,
            new BlobStorageOptions
            {
                ServiceUri = serviceUri,
                ConnectionString = connectionString,
                DocumentsContainer = "documents",
            });

    // ---- The production path ------------------------------------------------

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public void ServiceUri_WithoutConnectionString_IsValidInEveryEnvironment(string environment)
    {
        Validate(environment, serviceUri: "https://contoso.blob.core.windows.net/")
            .Succeeded.Should().BeTrue("the ServiceUri path is the default everywhere, Development included");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingServiceUri_FailsWithTheOriginalRequiredMessage(string serviceUri)
    {
        ValidateOptionsResult result = Validate("Production", serviceUri: serviceUri);

        result.Failed.Should().BeTrue();
        result.Failures.Should().Equal("Azure:Storage:ServiceUri must be configured.");
    }

    [Fact]
    public void RelativeServiceUri_FailsWithTheOriginalUrlMessage()
    {
        ValidateOptionsResult result = Validate("Production", serviceUri: "contoso.blob.core.windows.net");

        result.Failed.Should().BeTrue();
        result.Failures.Should().Equal("Azure:Storage:ServiceUri must be an absolute URL.");
    }

    // ---- The Azurite path ---------------------------------------------------

    [Theory]
    [InlineData("UseDevelopmentStorage=true")]
    [InlineData("usedevelopmentstorage=TRUE")]
    [InlineData(LoopbackAzurite)]
    [InlineData(ComposeAzurite)]
    public void AzuriteConnectionString_InDevelopment_IsValid(string connectionString)
    {
        Validate("Development", connectionString: connectionString)
            .Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public void AzuriteConnectionString_OutsideDevelopment_IsRefused(string environment)
    {
        ValidateOptionsResult result = Validate(environment, connectionString: "UseDevelopmentStorage=true");

        result.Failed.Should().BeTrue("a deployed host must never authenticate to storage with a key");
        result.FailureMessage.Should().Contain("only permitted in the Development environment")
            .And.Contain(environment);
    }

    [Fact]
    public void ConnectionStringAndServiceUri_Together_AreRefused()
    {
        ValidateOptionsResult result = Validate(
            "Development",
            serviceUri: "https://contoso.blob.core.windows.net/",
            connectionString: "UseDevelopmentStorage=true");

        result.Failed.Should().BeTrue("which account the process writes to must never depend on precedence");
        result.FailureMessage.Should().Contain("both set");
    }

    [Theory]
    [InlineData(
        "DefaultEndpointsProtocol=https;AccountName=contoso;AccountKey=cmVhbC1rZXk=;EndpointSuffix=core.windows.net",
        "a real account with a real key")]
    [InlineData(
        "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=cmVhbC1rZXk=;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;",
        "the emulator account name with a key that is not Azurite's")]
    [InlineData(
        $"AccountName=devstoreaccount1;AccountKey={AzuriteKey};",
        "no BlobEndpoint, which the SDK resolves to devstoreaccount1.blob.core.windows.net")]
    [InlineData(
        "BlobEndpoint=https://contoso.blob.core.windows.net/;SharedAccessSignature=sv=2026-06-06&sig=abc",
        "a SAS token, which is an issued credential")]
    [InlineData(
        $"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey={AzuriteKey};BlobEndpoint=http://azurite:10000/devstoreaccount1;SharedAccessSignature=sv=x",
        "Azurite's key alongside a SAS token")]
    [InlineData("UseDevelopmentStorage=false", "development storage explicitly turned off")]
    [InlineData(" UseDevelopmentStorage = true ", "whitespace around '=', which the SDK rejects")]
    [InlineData("UseDevelopmentStorage=true;UseDevelopmentStorage=true", "a duplicated setting")]
    [InlineData("not a connection string", "text that is not a connection string")]
    public void NonAzuriteConnectionString_IsRefusedEvenInDevelopment(string connectionString, string because)
    {
        ValidateOptionsResult result = Validate("Development", connectionString: connectionString);

        result.Failed.Should().BeTrue(because);
        result.FailureMessage.Should().Contain("must target the Azurite emulator");
    }
}
