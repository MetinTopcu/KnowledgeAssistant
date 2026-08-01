using Azure.Identity;

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
internal static class AzureCredentialFactory
{
    /// <summary>
    /// Creates a <see cref="DefaultAzureCredential"/> from configuration.
    /// </summary>
    /// <remarks>
    /// <c>DefaultAzureCredential</c> rather than an explicit credential type so
    /// the same code path runs everywhere: the chain resolves to the Azure CLI or
    /// Visual Studio identity on a laptop and to managed identity in Azure. No key
    /// exists in either environment, so there is none to leak.
    /// </remarks>
    internal static DefaultAzureCredential Create(AzureCredentialOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

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
