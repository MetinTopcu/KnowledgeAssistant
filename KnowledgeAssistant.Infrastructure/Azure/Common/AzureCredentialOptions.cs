namespace KnowledgeAssistant.Infrastructure.Azure.Common;

/// <summary>
/// Configuration for the Entra ID credential shared by every Azure client,
/// bound from the <c>Azure:Credential</c> section.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the credential is not a per-adapter setting.</b>
/// <c>AzureClientFactoryBuilder.UseCredential</c> applies one
/// <c>TokenCredential</c> to every client registered in the builder — Blob today,
/// Search and AI Foundry later. Hanging the client id off
/// <c>BlobStorageOptions</c> therefore let one adapter's configuration silently
/// govern all of them, and would have forced the same value to be repeated in
/// three sections once the other adapters landed.
/// </para>
/// <para>
/// This is also the key <c>CONFIGURATION.md</c> and <c>appsettings.json</c> have
/// documented from the start (<c>Azure__Credential__ManagedIdentityClientId</c>).
/// Binding it here is what makes the documented setting actually take effect.
/// </para>
/// <para>
/// <b>Nothing here is required.</b> The whole section may be absent: the
/// credential chain then resolves to the developer's own identity locally and to
/// the system-assigned managed identity in Azure. There is deliberately no
/// <c>[Required]</c> attribute — a missing value is the normal case, not a
/// misconfiguration, so failing startup over it would be wrong.
/// </para>
/// </remarks>
public sealed class AzureCredentialOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Azure:Credential";

    /// <summary>
    /// The client id of a user-assigned managed identity, or empty to use the
    /// system-assigned identity and the rest of the credential chain.
    /// </summary>
    /// <remarks>
    /// Not a secret — a client id is an identifier, not a credential. It is only
    /// needed when a host carries several user-assigned identities and the
    /// credential chain cannot infer which one to present. Left unset in that
    /// situation, the resulting failure reads as a permissions problem rather
    /// than an ambiguity, which is an expensive thing to debug.
    /// </remarks>
    public string ManagedIdentityClientId { get; init; } = string.Empty;

    /// <summary>
    /// Which single credential the Development environment uses. Ignored in
    /// every other environment, which always uses <c>DefaultAzureCredential</c>.
    /// </summary>
    /// <remarks>
    /// <c>AzureCli</c> for a host-run process; <c>ManagedIdentity</c> for the
    /// Docker Compose stack, whose container has no <c>az</c> and receives tokens
    /// from a local stand-in for the platform identity endpoint. Neither is a
    /// secret, and neither can be selected in production by accident.
    /// </remarks>
    public DevelopmentCredential DevelopmentCredential { get; init; } = DevelopmentCredential.AzureCli;
}

/// <summary>The credential a Development host authenticates with.</summary>
public enum DevelopmentCredential
{
    /// <summary>The Azure CLI login on the machine running the process.</summary>
    AzureCli,

    /// <summary>
    /// The managed identity protocol, answered locally by a development token
    /// endpoint (<c>IDENTITY_ENDPOINT</c> / <c>IDENTITY_HEADER</c>).
    /// </summary>
    ManagedIdentity,
}
