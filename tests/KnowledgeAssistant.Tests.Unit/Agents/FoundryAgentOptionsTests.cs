using System.ComponentModel.DataAnnotations;
using KnowledgeAssistant.Infrastructure.Azure.Agents;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace KnowledgeAssistant.Tests.Unit.Agents;

/// <summary>
/// Which agent configurations the host may start with.
/// </summary>
/// <remarks>
/// Two rules, both learned against the live service. The project endpoint is
/// required and must address a project, because the account endpoint answered
/// 404 to every agent call. And a deployed host must pin its agent version,
/// because an unpinned host decides its own agent definition at run time.
/// </remarks>
public sealed class FoundryAgentOptionsTests
{
    private const string ValidProjectEndpoint = "https://contoso.services.ai.azure.com/api/projects/my-project";

    private sealed class StubHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "KnowledgeAssistant.Api";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static List<ValidationResult> Validate(FoundryAgentOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }

    // ---- ProjectEndpoint ------------------------------------------------------

    [Fact]
    public void AProjectEndpoint_IsValid()
    {
        Validate(new FoundryAgentOptions { ProjectEndpoint = ValidProjectEndpoint }).Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AMissingProjectEndpoint_FailsOnceWithTheExpectedShape(string endpoint)
    {
        Validate(new FoundryAgentOptions { ProjectEndpoint = endpoint }).Should().ContainSingle()
            .Which.ErrorMessage.Should().Contain("ProjectEndpoint must be configured")
            .And.Contain("/api/projects/<project>");
    }

    [Theory]
    [InlineData("https://contoso.services.ai.azure.com/")]
    [InlineData("https://contoso.services.ai.azure.com")]
    [InlineData("https://contoso.services.ai.azure.com/api/projects/")]
    [InlineData("https://contoso.services.ai.azure.com/api/projects/my-project/agents")]
    [InlineData("http://contoso.services.ai.azure.com/api/projects/my-project")]
    [InlineData("contoso.services.ai.azure.com/api/projects/my-project")]
    public void AnEndpointThatIsNotAProjectEndpoint_FailsAtStartup(string endpoint)
    {
        Validate(new FoundryAgentOptions { ProjectEndpoint = endpoint }).Should().ContainSingle()
            .Which.ErrorMessage.Should().Contain("the account endpoint does not serve agents");
    }

    [Theory]
    [InlineData(ValidProjectEndpoint, "my-project")]
    [InlineData("https://contoso.services.ai.azure.com/api/projects/my-project/", "my-project")]
    public void TheProjectName_IsTakenFromThePath(string endpoint, string expected)
    {
        FoundryAgentOptions.TryGetProjectName(endpoint, out string name).Should().BeTrue();
        name.Should().Be(expected);
    }

    // ---- Temperature -----------------------------------------------------------

    [Fact]
    public void Temperature_IsUnsetByDefault()
    {
        new FoundryAgentOptions().Temperature.Should().BeNull(
            "reasoning models reject any temperature but their default");
    }

    [Fact]
    public void ATemperatureOutOfRange_IsRejected()
    {
        Validate(new FoundryAgentOptions { ProjectEndpoint = ValidProjectEndpoint, Temperature = 3.0 })
            .Should().ContainSingle()
            .Which.ErrorMessage.Should().Contain("Agent:Temperature");
    }

    // ---- Version pinning -------------------------------------------------------

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public void OutsideDevelopment_AnUnpinnedVersionFails(string environment)
    {
        ValidateOptionsResult result = new FoundryAgentOptionsValidator(new StubHostEnvironment(environment))
            .Validate(Options.DefaultName, new FoundryAgentOptions { ProjectEndpoint = ValidProjectEndpoint });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Azure:AiFoundry:Agent:Version must be set outside Development")
            .And.Contain(environment);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public void APinnedVersion_IsValidEverywhere(string environment)
    {
        new FoundryAgentOptionsValidator(new StubHostEnvironment(environment))
            .Validate(Options.DefaultName, new FoundryAgentOptions { ProjectEndpoint = ValidProjectEndpoint, Version = "1" })
            .Succeeded.Should().BeTrue();
    }

    [Fact]
    public void InDevelopment_AnUnpinnedVersionIsAllowedSoOneCanBeProvisioned()
    {
        new FoundryAgentOptionsValidator(new StubHostEnvironment("Development"))
            .Validate(Options.DefaultName, new FoundryAgentOptions { ProjectEndpoint = ValidProjectEndpoint })
            .Succeeded.Should().BeTrue();
    }
}
