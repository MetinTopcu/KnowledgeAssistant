using System.ClientModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Azure.AI.Projects.Agents;
using Azure.Identity;
using KnowledgeAssistant.Application.Agents;
using KnowledgeAssistant.Domain.Common;
using Microsoft.Extensions.Logging;
using Polly;

namespace KnowledgeAssistant.Infrastructure.Azure.Agents;

/// <summary>
/// Resolves the agent version this process answers with, creating one if needed.
/// </summary>
/// <remarks>
/// <para>
/// <b>The problem this solves.</b> An agent in Foundry is a named, versioned
/// definition — model, instructions, tools — that must exist before it can be
/// run. The obvious implementations are both wrong. Creating a version at startup
/// mints one per process: with three instances and a daily deploy that is
/// ninety-odd identical versions a month, and the portal becomes unusable.
/// Requiring one to exist makes the service unusable from a clean project without
/// a separate provisioning step nobody documents.
/// </para>
/// <para>
/// <b>What it does instead.</b> The definition is hashed — model, instructions,
/// temperature, and the tool's full schema — and the hash is stamped into the
/// version's metadata. On first use the existing versions are scanned newest-first
/// for a matching hash: found, it is reused; absent, exactly one version is
/// created. So a fleet of instances converges on one version, a redeploy that
/// changed nothing creates nothing, and editing the instructions creates a new
/// version precisely because the instructions changed.
/// </para>
/// <para>
/// <b>The race is benign and deliberately not locked against.</b> Two instances
/// starting together can both find nothing and both create a version. The result
/// is two identical versions, either of which answers identically — which is a
/// better outcome than a distributed lock held across a network call, and far
/// better than a startup that fails because another instance was first.
/// </para>
/// <para>
/// <b>Pinning short-circuits all of it.</b> With <c>Azure:AiFoundry:Agent:Version</c>
/// set, nothing is listed and nothing is created, and the adapter needs no write
/// permission on the project. That is the production arrangement; this one exists
/// so the first run works.
/// </para>
/// <para>
/// <b>Registered as a singleton</b>, which is what makes the resolution happen
/// once. A shorter lifetime would re-list versions on every question.
/// </para>
/// </remarks>
internal sealed partial class FoundryAgentProvisioner : IDisposable
{
    /// <summary>The metadata key the definition hash is stored under.</summary>
    private const string FingerprintKey = "definition_fingerprint";

    /// <summary>How many recent versions are examined for a match.</summary>
    /// <remarks>
    /// Bounded because the scan runs before the first question is answered and a
    /// project with a long history would otherwise page through all of it. Versions
    /// are listed newest-first, so a definition in current use is found in the
    /// first page; one that has not been deployed in fifty revisions is better
    /// recreated than waited for.
    /// </remarks>
    private const int VersionScanLimit = 50;

    private readonly AgentAdministrationClient _administrationClient;
    private readonly FoundryAgentOptions _options;
    private readonly string _modelDeploymentName;
    private readonly ResiliencePipeline _resiliencePipeline;
    private readonly ILogger<FoundryAgentProvisioner> _logger;

    // Guards the one-time resolution. Not a Lazy<Task>, which would cache a
    // faulted task permanently and let one transient startup failure poison every
    // later question for the lifetime of the process — the same reasoning as the
    // search index's existence check.
    private readonly SemaphoreSlim _resolutionLock = new(1, 1);
    private volatile string? _resolvedVersion;

    /// <summary>Initialises the provisioner.</summary>
    public FoundryAgentProvisioner(
        AgentAdministrationClient administrationClient,
        FoundryAgentOptions options,
        string modelDeploymentName,
        ResiliencePipeline resiliencePipeline,
        ILogger<FoundryAgentProvisioner> logger)
    {
        ArgumentNullException.ThrowIfNull(administrationClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(resiliencePipeline);

        _administrationClient = administrationClient;
        _options = options;
        _modelDeploymentName = modelDeploymentName;
        _resiliencePipeline = resiliencePipeline;
        _logger = logger;
    }

    /// <summary>
    /// The hash of the definition this process would provision.
    /// </summary>
    /// <remarks>
    /// Computed from every input that changes the agent's behaviour and from
    /// nothing else. The tool schema is included in full: a new argument the model
    /// may send is a behavioural change even though no instruction text moved, and
    /// omitting it would leave the fleet running an agent whose tool contract no
    /// longer matches the code that answers its calls.
    /// </remarks>
    internal string DefinitionFingerprint => Fingerprint(
        _modelDeploymentName,
        AgentInstructions.SystemPrompt,
        _options.Temperature,
        KnowledgeSearchTool.FunctionName,
        KnowledgeSearchTool.FunctionDescription,
        KnowledgeSearchTool.ParameterSchema);

    /// <summary>
    /// Returns the agent version to run, resolving it on the first call.
    /// </summary>
    /// <remarks>
    /// A pinned version goes through the same once-per-process gate: it is
    /// verified against this build's definition before it is first used, rather
    /// than trusted because configuration named it.
    /// </remarks>
    internal async Task<Result<string>> ResolveVersionAsync(CancellationToken cancellationToken)
    {
        string? resolved = _resolvedVersion;

        if (resolved is not null)
        {
            return resolved;
        }

        await _resolutionLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // Re-checked inside the lock: several questions can arrive together on
            // a cold process, and without this every one of them would list and
            // possibly create a version.
            resolved = _resolvedVersion;

            if (resolved is not null)
            {
                return resolved;
            }

            Result<string> result = await ResolveCoreAsync(cancellationToken).ConfigureAwait(false);

            if (result.IsSuccess)
            {
                _resolvedVersion = result.Value;
            }

            return result;
        }
        finally
        {
            _resolutionLock.Release();
        }
    }

    /// <summary>
    /// Verifies the pinned version, or finds a matching version or creates one.
    /// </summary>
    private async Task<Result<string>> ResolveCoreAsync(CancellationToken cancellationToken)
    {
        string fingerprint = DefinitionFingerprint;

        try
        {
            if (_options.IsVersionPinned)
            {
                return await VerifyPinnedVersionAsync(fingerprint, cancellationToken).ConfigureAwait(false);
            }

            string? existing = await FindMatchingVersionAsync(fingerprint, cancellationToken)
                .ConfigureAwait(false);

            if (existing is not null)
            {
                LogVersionReused(_options.Name, existing, fingerprint);
                return existing;
            }

            ProjectsAgentVersion created = await CreateVersionAsync(fingerprint, cancellationToken)
                .ConfigureAwait(false);

            LogVersionCreated(_options.Name, created.Version, fingerprint);

            return created.Version;
        }
        catch (ClientResultException exception) when (exception.Status == 429)
        {
            LogProvisioningRateLimited(exception, _options.Name);
            return Result.Failure<string>(AgentErrors.RateLimited);
        }
        catch (ClientResultException exception)
        {
            LogProvisioningFailed(exception, _options.Name, exception.Status);
            return Result.Failure<string>(AgentErrors.ProvisioningFailed);
        }
        // Covers CredentialUnavailableException too, which derives from it.
        catch (AuthenticationFailedException exception)
        {
            LogAuthenticationFailed(exception);
            return Result.Failure<string>(AgentErrors.AuthenticationFailed);
        }
        catch (HttpRequestException exception)
        {
            LogProvisioningUnreachable(exception, _options.Name);
            return Result.Failure<string>(AgentErrors.ProvisioningFailed);
        }
    }

    /// <summary>
    /// Scans recent versions for one whose definition matches this build's.
    /// </summary>
    /// <remarks>
    /// A 404 means the agent has never existed, which is the ordinary first-run
    /// case rather than a fault: it is translated into "no match" so the caller
    /// creates one. Every other status is left to propagate, because an agent that
    /// exists but cannot be listed is a permissions problem, and silently creating
    /// a duplicate on top of it would replace a clear error with a confusing one.
    /// </remarks>
    private async Task<string?> FindMatchingVersionAsync(
        string fingerprint,
        CancellationToken cancellationToken)
    {
        try
        {
            AsyncCollectionResult<ProjectsAgentVersion> versions = _administrationClient
                .GetAgentVersionsAsync(
                    _options.Name,
                    limit: VersionScanLimit,
                    order: AgentListOrder.Descending,
                    after: null,
                    before: null,
                    cancellationToken);

            await foreach (ProjectsAgentVersion version in versions.ConfigureAwait(false))
            {
                if (version.Metadata is not null &&
                    version.Metadata.TryGetValue(FingerprintKey, out string? stored) &&
                    string.Equals(stored, fingerprint, StringComparison.Ordinal))
                {
                    return version.Version;
                }
            }

            return null;
        }
        catch (ClientResultException exception) when (exception.Status == 404)
        {
            LogAgentNotFound(_options.Name);
            return null;
        }
    }

    /// <summary>
    /// Confirms the pinned version exists and carries this build's definition.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The agent runs the model, instructions, and tool schema stored in its
    /// version, not those in this process's configuration. A pinned version made
    /// from a different definition would therefore answer with a tool contract or
    /// model this code was not written against — silently, because every call
    /// would still succeed. Comparing fingerprints turns that into a named
    /// failure on the first question.
    /// </para>
    /// <para>
    /// Only a read: verification needs no write role, which is the point of
    /// pinning. Failures are not cached, so a corrected configuration takes
    /// effect on the next restart without anything to clear.
    /// </para>
    /// </remarks>
    private async Task<Result<string>> VerifyPinnedVersionAsync(
        string fingerprint,
        CancellationToken cancellationToken)
    {
        ProjectsAgentVersion pinned;

        try
        {
            pinned = await _resiliencePipeline.ExecuteAsync(
                async token =>
                {
                    ClientResult<ProjectsAgentVersion> result = await _administrationClient
                        .GetAgentVersionAsync(_options.Name, _options.Version, token)
                        .ConfigureAwait(false);

                    return result.Value;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (ClientResultException exception) when (exception.Status == 404)
        {
            LogPinnedVersionNotFound(exception, _options.Name, _options.Version);
            return Result.Failure<string>(AgentErrors.ProvisioningFailed);
        }

        string? stored = null;

        if (pinned.Metadata is not null)
        {
            pinned.Metadata.TryGetValue(FingerprintKey, out stored);
        }

        if (!string.Equals(stored, fingerprint, StringComparison.Ordinal))
        {
            LogPinnedVersionMismatch(_options.Name, _options.Version, stored ?? "(none)", fingerprint);
            return Result.Failure<string>(AgentErrors.ProvisioningFailed);
        }

        LogPinnedVersionVerified(_options.Name, _options.Version, fingerprint);
        return _options.Version;
    }

    /// <summary>Creates a version carrying this build's definition.</summary>
    /// <remarks>
    /// <para>
    /// The tool is attached here, and this is the only place in the solution where
    /// that happens. What the agent can do is fixed at provisioning time rather
    /// than negotiated per request, which is what makes the fingerprint a complete
    /// description of the agent's behaviour.
    /// </para>
    /// <para>
    /// Wrapped in the shared resilience pipeline like every other Foundry call:
    /// provisioning happens on the first question, so a transient failure here is
    /// a failure a user is waiting on rather than a startup inconvenience.
    /// </para>
    /// </remarks>
    private async Task<ProjectsAgentVersion> CreateVersionAsync(
        string fingerprint,
        CancellationToken cancellationToken)
    {
        var definition = new DeclarativeAgentDefinition(_modelDeploymentName)
        {
            Instructions = AgentInstructions.SystemPrompt,
            // Null leaves temperature out of the definition, so the model's
            // default applies; reasoning models accept nothing else.
            Temperature = (float?)_options.Temperature,
        };

        definition.Tools.Add(KnowledgeSearchTool.CreateDefinition());

        var creationOptions = new ProjectsAgentVersionCreationOptions(definition)
        {
            Description = AgentInstructions.Description,
        };

        creationOptions.Metadata[FingerprintKey] = fingerprint;

        return await _resiliencePipeline.ExecuteAsync(
            async token =>
            {
                ClientResult<ProjectsAgentVersion> result = await _administrationClient
                    .CreateAgentVersionAsync(_options.Name, creationOptions, cancellationToken: token)
                    .ConfigureAwait(false);

                return result.Value;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Hashes the inputs that define the agent's behaviour.</summary>
    /// <remarks>
    /// SHA-256 over the parts joined by a newline, which cannot appear in any of
    /// them except the instructions — and the instructions are last, so no two
    /// distinct definitions can produce the same joined string. Not a security
    /// boundary: this identifies a configuration, and the only thing a collision
    /// could achieve is reusing an agent version that is already public in the
    /// project.
    /// </remarks>
    private static string Fingerprint(
        string model,
        string instructions,
        double? temperature,
        string functionName,
        string functionDescription,
        string parameterSchema)
    {
        string material = string.Join(
            '\n',
            model,

            // "default" cannot collide with a formatted number.
            temperature?.ToString("R", CultureInfo.InvariantCulture) ?? "default",
            functionName,
            parameterSchema,
            functionDescription,
            instructions);

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));

        return Convert.ToHexStringLower(hash);
    }

    /// <inheritdoc />
    public void Dispose() => _resolutionLock.Dispose();

    [LoggerMessage(
        EventId = 5310,
        Level = LogLevel.Information,
        Message = "Reusing Foundry agent '{AgentName}' version {AgentVersion} (definition {Fingerprint}).")]
    private partial void LogVersionReused(string agentName, string agentVersion, string fingerprint);

    [LoggerMessage(
        EventId = 5311,
        Level = LogLevel.Information,
        Message = "Created Foundry agent '{AgentName}' version {AgentVersion} (definition {Fingerprint}). " +
                  "Pin this version in Azure:AiFoundry:Agent:Version to stop the running process provisioning.")]
    private partial void LogVersionCreated(string agentName, string agentVersion, string fingerprint);

    [LoggerMessage(
        EventId = 5312,
        Level = LogLevel.Information,
        Message = "Foundry agent '{AgentName}' does not exist yet; it will be created.")]
    private partial void LogAgentNotFound(string agentName);

    [LoggerMessage(
        EventId = 5313,
        Level = LogLevel.Error,
        Message = "Could not resolve Foundry agent '{AgentName}': the project returned status {Status}. " +
                  "A 404 usually means Azure:AiFoundry:Agent:ProjectEndpoint is not a project endpoint " +
                  "(.../api/projects/<name>); a 401 or 403 means the identity lacks Azure AI Project Manager " +
                  "on the project, or pin an existing version in Azure:AiFoundry:Agent:Version.")]
    private partial void LogProvisioningFailed(Exception exception, string agentName, int status);

    [LoggerMessage(
        EventId = 5314,
        Level = LogLevel.Error,
        Message = "Provisioning Foundry agent '{AgentName}' is rate limited; retries were exhausted.")]
    private partial void LogProvisioningRateLimited(Exception exception, string agentName);

    [LoggerMessage(
        EventId = 5315,
        Level = LogLevel.Error,
        Message = "Failed to authenticate to Azure AI Foundry. Verify the managed identity and its RBAC role assignments.")]
    private partial void LogAuthenticationFailed(Exception exception);

    [LoggerMessage(
        EventId = 5316,
        Level = LogLevel.Error,
        Message = "Azure AI Foundry was unreachable while resolving agent '{AgentName}'.")]
    private partial void LogProvisioningUnreachable(Exception exception, string agentName);

    [LoggerMessage(
        EventId = 5317,
        Level = LogLevel.Information,
        Message = "Using pinned Foundry agent '{AgentName}' version {AgentVersion}; its definition matches this build ({Fingerprint}).")]
    private partial void LogPinnedVersionVerified(string agentName, string agentVersion, string fingerprint);

    [LoggerMessage(
        EventId = 5318,
        Level = LogLevel.Error,
        Message = "Pinned Foundry agent '{AgentName}' version {AgentVersion} does not exist in the project. " +
                  "Check Azure:AiFoundry:Agent:Version and Azure:AiFoundry:Agent:ProjectEndpoint.")]
    private partial void LogPinnedVersionNotFound(Exception exception, string agentName, string agentVersion);

    [LoggerMessage(
        EventId = 5319,
        Level = LogLevel.Error,
        Message = "Pinned Foundry agent '{AgentName}' version {AgentVersion} was created from definition {StoredFingerprint}, " +
                  "but this build defines {ExpectedFingerprint}. The instructions, tool schema, model deployment, or " +
                  "temperature differ. Provision a matching version in Development and pin that one.")]
    private partial void LogPinnedVersionMismatch(
        string agentName,
        string agentVersion,
        string storedFingerprint,
        string expectedFingerprint);
}
