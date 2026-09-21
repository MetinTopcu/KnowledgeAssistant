using Azure.Core;
using Azure.Identity;
using KnowledgeAssistant.Infrastructure.Azure.Common;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace KnowledgeAssistant.Tests.Unit.Credentials;

/// <summary>
/// Which credential each host environment authenticates with.
/// </summary>
/// <remarks>
/// The rule protected here is one-sided: only Development may leave the
/// <c>DefaultAzureCredential</c> chain. Every other environment name — including
/// ones this project does not use yet — must still get the chain, because that
/// is what finds the managed identity in Azure. Constructing either credential
/// makes no network call, so these tests touch nothing outside the process.
/// </remarks>
public sealed class AzureCredentialFactoryTests
{
    private sealed class StubHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "KnowledgeAssistant.Api";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("development")]
    public void InDevelopment_TheAzureCliLoginIsUsedDirectly(string environment)
    {
        TokenCredential credential = AzureCredentialFactory.Create(
            new AzureCredentialOptions(),
            new StubHostEnvironment(environment));

        credential.Should().BeOfType<AzureCliCredential>();
    }

    [Fact]
    public void InDevelopment_AManagedIdentityClientIdDoesNotBringTheChainBack()
    {
        TokenCredential credential = AzureCredentialFactory.Create(
            new AzureCredentialOptions { ManagedIdentityClientId = "00000000-0000-0000-0000-000000000001" },
            new StubHostEnvironment("Development"));

        credential.Should().BeOfType<AzureCliCredential>();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Test")]
    public void OutsideDevelopment_TheDefaultChainIsUsed(string environment)
    {
        TokenCredential credential = AzureCredentialFactory.Create(
            new AzureCredentialOptions(),
            new StubHostEnvironment(environment));

        credential.Should().BeOfType<DefaultAzureCredential>();
    }

    [Fact]
    public void InProduction_AUserAssignedIdentityStillGetsTheDefaultChain()
    {
        TokenCredential credential = AzureCredentialFactory.Create(
            new AzureCredentialOptions { ManagedIdentityClientId = "00000000-0000-0000-0000-000000000001" },
            new StubHostEnvironment("Production"));

        credential.Should().BeOfType<DefaultAzureCredential>();
    }

    [Fact]
    public void InDevelopment_ManagedIdentityCanBeSelectedForTheComposeStack()
    {
        TokenCredential credential = AzureCredentialFactory.Create(
            new AzureCredentialOptions { DevelopmentCredential = DevelopmentCredential.ManagedIdentity },
            new StubHostEnvironment("Development"));

        credential.Should().BeOfType<ManagedIdentityCredential>(
            "one explicit credential, not the chain whose probing was measured at 27 s");
    }

    [Theory]
    [InlineData(DevelopmentCredential.AzureCli)]
    [InlineData(DevelopmentCredential.ManagedIdentity)]
    public void OutsideDevelopment_TheDevelopmentCredentialSettingIsIgnored(DevelopmentCredential setting)
    {
        TokenCredential credential = AzureCredentialFactory.Create(
            new AzureCredentialOptions { DevelopmentCredential = setting },
            new StubHostEnvironment("Production"));

        credential.Should().BeOfType<DefaultAzureCredential>();
    }

    [Fact]
    public void TheDevelopmentCredential_DefaultsToTheAzureCli()
    {
        new AzureCredentialOptions().DevelopmentCredential.Should().Be(DevelopmentCredential.AzureCli);
    }

    [Fact]
    public void NullOptions_AreRejected()
    {
        Action act = () => AzureCredentialFactory.Create(null!, new StubHostEnvironment("Production"));

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void NullEnvironment_IsRejected()
    {
        Action act = () => AzureCredentialFactory.Create(new AzureCredentialOptions(), null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("environment");
    }
}
