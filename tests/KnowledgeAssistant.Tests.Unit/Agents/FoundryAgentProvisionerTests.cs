using System.ClientModel;
using Azure.AI.Projects.Agents;
using KnowledgeAssistant.Domain.Common;
using KnowledgeAssistant.Infrastructure.Azure.Agents;
using KnowledgeAssistant.Tests.Unit.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;

namespace KnowledgeAssistant.Tests.Unit.Agents;

/// <summary>
/// How a pinned agent version is resolved.
/// </summary>
/// <remarks>
/// The agent runs the definition stored in its version, not the one in this
/// process's configuration. A pinned version is therefore verified against this
/// build's definition fingerprint before it is used, and a mismatch or a missing
/// version fails instead of silently running a different agent.
/// </remarks>
public sealed class FoundryAgentProvisionerTests
{
    private const string AgentName = "knowledge-assistant";

    private sealed class FakeAdministrationClient : AgentAdministrationClient
    {
        private readonly Func<string, string, ProjectsAgentVersion> _getVersion;

        public FakeAdministrationClient(Func<string, string, ProjectsAgentVersion> getVersion) =>
            _getVersion = getVersion;

        public int GetVersionCalls { get; private set; }

        public int ListCalls { get; private set; }

        public override Task<ClientResult<ProjectsAgentVersion>> GetAgentVersionAsync(
            string agentName,
            string agentVersion,
            CancellationToken cancellationToken = default)
        {
            GetVersionCalls++;
            return Task.FromResult(ClientResult.FromValue(
                _getVersion(agentName, agentVersion),
                new FakePipelineResponse(200)));
        }

        public override AsyncCollectionResult<ProjectsAgentVersion> GetAgentVersionsAsync(
            string agentName,
            int? limit = null,
            AgentListOrder? order = null,
            string? after = null,
            string? before = null,
            CancellationToken cancellationToken = default)
        {
            ListCalls++;
            throw new InvalidOperationException("A pinned version must not be resolved by listing.");
        }
    }

    private static FoundryAgentProvisioner Provisioner(FakeAdministrationClient client, string version = "7") =>
        new(
            client,
            new FoundryAgentOptions
            {
                ProjectEndpoint = "https://contoso.services.ai.azure.com/api/projects/p",
                Name = AgentName,
                Version = version,
            },
            "gpt-5-mini",
            ResiliencePipeline.Empty,
            NullLogger<FoundryAgentProvisioner>.Instance);

    private static ProjectsAgentVersion VersionWithFingerprint(string version, string? fingerprint)
    {
        var metadata = new Dictionary<string, string>();

        if (fingerprint is not null)
        {
            metadata["definition_fingerprint"] = fingerprint;
        }

        return ProjectsAgentsModelFactory.ProjectsAgentVersion(
            metadata: metadata,
            id: $"{AgentName}:{version}",
            name: AgentName,
            version: version,
            description: "test",
            createdAt: DateTimeOffset.UnixEpoch,
            definition: new DeclarativeAgentDefinition("gpt-5-mini"));
    }

    private static string ExpectedFingerprint()
    {
        using FoundryAgentProvisioner probe = Provisioner(new FakeAdministrationClient((_, v) => VersionWithFingerprint(v, null)));
        return probe.DefinitionFingerprint;
    }

    [Fact]
    public async Task APinnedVersionWithThisBuildsDefinition_IsUsed()
    {
        string fingerprint = ExpectedFingerprint();
        var client = new FakeAdministrationClient((_, v) => VersionWithFingerprint(v, fingerprint));
        using FoundryAgentProvisioner provisioner = Provisioner(client);

        Result<string> result = await provisioner.ResolveVersionAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("7");
        client.ListCalls.Should().Be(0, "a pinned version is read directly, never searched for or created");
    }

    [Fact]
    public async Task APinnedVersion_IsVerifiedOnlyOncePerProcess()
    {
        string fingerprint = ExpectedFingerprint();
        var client = new FakeAdministrationClient((_, v) => VersionWithFingerprint(v, fingerprint));
        using FoundryAgentProvisioner provisioner = Provisioner(client);

        await provisioner.ResolveVersionAsync(CancellationToken.None);
        await provisioner.ResolveVersionAsync(CancellationToken.None);

        client.GetVersionCalls.Should().Be(1);
    }

    [Fact]
    public async Task APinnedVersionFromADifferentDefinition_Fails()
    {
        var client = new FakeAdministrationClient((_, v) => VersionWithFingerprint(v, "someone-elses-definition"));
        using FoundryAgentProvisioner provisioner = Provisioner(client);

        Result<string> result = await provisioner.ResolveVersionAsync(CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AgentErrors.ProvisioningFailed);
    }

    [Fact]
    public async Task APinnedVersionWithoutAFingerprint_Fails()
    {
        // A version created by hand in the portal carries no fingerprint, so
        // nothing shows it matches this build.
        var client = new FakeAdministrationClient((_, v) => VersionWithFingerprint(v, null));
        using FoundryAgentProvisioner provisioner = Provisioner(client);

        (await provisioner.ResolveVersionAsync(CancellationToken.None)).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task APinnedVersionThatDoesNotExist_Fails()
    {
        var client = new FakeAdministrationClient((_, _) =>
            throw new ClientResultException("not found", new FakePipelineResponse(404), null));
        using FoundryAgentProvisioner provisioner = Provisioner(client);

        Result<string> result = await provisioner.ResolveVersionAsync(CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AgentErrors.ProvisioningFailed);
    }

    [Fact]
    public async Task AFailedVerification_IsNotCached()
    {
        string fingerprint = ExpectedFingerprint();
        int calls = 0;
        var client = new FakeAdministrationClient((_, v) =>
            VersionWithFingerprint(v, ++calls == 1 ? "stale" : fingerprint));
        using FoundryAgentProvisioner provisioner = Provisioner(client);

        (await provisioner.ResolveVersionAsync(CancellationToken.None)).IsFailure.Should().BeTrue();
        (await provisioner.ResolveVersionAsync(CancellationToken.None)).IsSuccess.Should().BeTrue();
    }
}
