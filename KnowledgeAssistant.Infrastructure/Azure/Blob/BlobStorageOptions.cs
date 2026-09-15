using System.ComponentModel.DataAnnotations;

namespace KnowledgeAssistant.Infrastructure.Azure.Blob;

/// <summary>
/// Configuration for the Azure Blob Storage adapter, bound from the
/// <c>Azure:Storage</c> section.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this lives in Infrastructure rather than <c>Api/Configuration</c>.</b>
/// Sprint 1's <c>Api/Configuration/README.md</c> listed <c>BlobStorageOptions</c>
/// as belonging to the API project. That was a mistake: Infrastructure would
/// have to reference the API to consume it, inverting the dependency rule and
/// making the outermost layer a dependency of an inner one. Options that
/// configure an adapter belong beside the adapter. <c>Api/Configuration</c>
/// keeps host-level settings the web layer itself owns.
/// </para>
/// <para>
/// <b>No account key in any real environment.</b> A connection string embeds an
/// account key, which is the credential this design is built to avoid holding.
/// Authentication against Azure is Entra ID via <c>DefaultAzureCredential</c>,
/// so the only configuration needed is where the account is and which container
/// to use — neither of which is a secret.
/// </para>
/// <para>
/// <b>The one exception is the local emulator.</b> <see cref="ConnectionString"/>
/// exists so a laptop can run against Azurite, which accepts only shared-key
/// authentication over plain HTTP. It cannot become a back door for a real key:
/// <see cref="BlobStorageOptionsValidator"/> refuses it outside the Development
/// environment, and refuses any connection string that is not Azurite's own
/// <c>devstoreaccount1</c> with the key Microsoft publishes in its documentation.
/// </para>
/// <para>
/// <b>Nor does the credential itself live here.</b> One <c>TokenCredential</c>
/// serves every Azure client, so it is configured once in
/// <see cref="Common.AzureCredentialOptions"/> from the <c>Azure:Credential</c>
/// section rather than repeated per adapter.
/// </para>
/// </remarks>
public sealed class BlobStorageOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Azure:Storage";

    /// <summary>
    /// The blob service endpoint, for example
    /// <c>https://contoso.blob.core.windows.net/</c>.
    /// </summary>
    /// <remarks>
    /// Required unless <see cref="ConnectionString"/> is set. The rule is
    /// conditional, so it is enforced by <see cref="BlobStorageOptionsValidator"/>
    /// rather than by attributes, with the same messages the attributes produced.
    /// </remarks>
    public string ServiceUri { get; init; } = string.Empty;

    /// <summary>
    /// An Azurite connection string, for local development only. Empty everywhere
    /// else.
    /// </summary>
    /// <remarks>
    /// Setting it replaces <see cref="ServiceUri"/> and
    /// <c>DefaultAzureCredential</c> for the blob client alone; every other Azure
    /// client is unaffected. Accepted only in the Development environment, and
    /// only for the <c>devstoreaccount1</c> emulator account — see
    /// <see cref="BlobStorageOptionsValidator"/>.
    /// </remarks>
    public string ConnectionString { get; init; } = string.Empty;

    /// <summary>The container documents are written to.</summary>
    /// <remarks>
    /// The pattern is Azure's own container naming rule — 3 to 63 characters,
    /// lowercase alphanumerics and single hyphens. Validating it here converts
    /// a typo from a 400 on the first upload into a startup failure with a
    /// message that names the setting.
    /// </remarks>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Azure:Storage:DocumentsContainer must be configured.")]
    [RegularExpression(
        "^[a-z0-9](?:[a-z0-9]|-(?=[a-z0-9])){1,61}[a-z0-9]$",
        ErrorMessage = "Azure:Storage:DocumentsContainer must be 3-63 characters of lowercase letters, digits, and non-consecutive hyphens.")]
    public string DocumentsContainer { get; init; } = string.Empty;

    /// <summary>
    /// Whether the blob client should be built from the Azurite connection string
    /// rather than from <see cref="ServiceUri"/> and the shared credential.
    /// </summary>
    public bool UsesDevelopmentStorage => !string.IsNullOrWhiteSpace(ConnectionString);
}
