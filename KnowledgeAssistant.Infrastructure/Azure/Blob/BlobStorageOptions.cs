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
/// <b>No key or connection string.</b> A connection string embeds an account
/// key, which is the credential this design is built to avoid holding.
/// Authentication is Entra ID via <c>DefaultAzureCredential</c>, so the only
/// configuration needed is where the account is and which container to use —
/// neither of which is a secret.
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
    [Required(AllowEmptyStrings = false, ErrorMessage = "Azure:Storage:ServiceUri must be configured.")]
    [Url(ErrorMessage = "Azure:Storage:ServiceUri must be an absolute URL.")]
    public string ServiceUri { get; init; } = string.Empty;

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
}
