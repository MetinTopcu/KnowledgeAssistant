using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Hosting;

namespace KnowledgeAssistant.Infrastructure.Azure.Common;

/// <summary>
/// Builds the <c>TokenCredential</c> every Azure client authenticates with.
/// </summary>
/// <remarks>
/// Lives in <c>Azure/Common</c> because that folder is, per its README, the home
/// for credential setup shared across services. Keeping it out of the DI
/// registration means the credential policy is one small testable unit rather
/// than a private method buried in a composition root.
/// </remarks>
/// <remarks>
/// <para>
/// <b>Public as of the observability work.</b> The telemetry exporter in the API
/// project authenticates to Azure Monitor with Entra ID like everything else
/// here, so it needs this same policy. The alternative was to copy these few
/// lines into the composition root, which is how two credential configurations
/// drift until one deployment authenticates and the other does not.
/// </para>
/// <para>
/// This is a factory over an already-public options type, not an adapter, so the
/// convention that adapters stay internal is untouched.
/// </para>
/// </remarks>
public static class AzureCredentialFactory
{
    /// <summary>
    /// Creates the credential for the given host environment: one explicit
    /// developer credential in Development, <see cref="DefaultAzureCredential"/>
    /// everywhere else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>DefaultAzureCredential</c> outside Development so the same code path
    /// runs in every deployed environment and resolves to managed identity in
    /// Azure. No key exists there, so there is none to leak.
    /// </para>
    /// <para>
    /// <b>Why Development skips the chain.</b> On a laptop the chain was measured
    /// (Azure.Identity 1.21.0 / Azure.Core 1.60.0) taking 26.8 s in
    /// <c>ManagedIdentityCredential</c> probing an unreachable IMDS endpoint, then
    /// failing hard instead of falling through; with managed identity excluded,
    /// <c>VisualStudioCredential</c> still spent 24 s failing before the Azure CLI
    /// produced the token. Development therefore uses exactly one credential,
    /// chosen by <see cref="AzureCredentialOptions.DevelopmentCredential"/>:
    /// </para>
    /// <list type="bullet">
    /// <item><c>AzureCli</c> (default) — the <c>az login</c> on the host, for
    /// <c>dotnet run</c>.</item>
    /// <item><c>ManagedIdentity</c> — a <see cref="ManagedIdentityCredential"/>
    /// with no probing chain, for Docker Compose, where the runtime image has no
    /// <c>az</c> and a local token endpoint stands in for the platform's
    /// (see <c>tools/azure-token-proxy</c>). It is the same credential type that
    /// answers in Azure, which is the point.</item>
    /// </list>
    /// <para>
    /// Outside Development the setting is ignored and the chain is used as-is.
    /// </para>
    /// </remarks>
    public static TokenCredential Create(AzureCredentialOptions options, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);

        if (environment.IsDevelopment())
        {
            return options.DevelopmentCredential == DevelopmentCredential.ManagedIdentity
                ? new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned)
                : new AzureCliCredential();
        }

        var credentialOptions = new DefaultAzureCredentialOptions();

        // Only set when a user-assigned identity is named. Assigning an empty
        // string is not equivalent to leaving it null — the SDK would treat it as
        // an explicit request for a user-assigned identity with a blank id and
        // fail the whole chain.
        if (!string.IsNullOrWhiteSpace(options.ManagedIdentityClientId))
        {
            credentialOptions.ManagedIdentityClientId = options.ManagedIdentityClientId;
        }

        return new DefaultAzureCredential(credentialOptions);
    }
}
