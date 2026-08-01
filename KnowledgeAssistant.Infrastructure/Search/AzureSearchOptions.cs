using System.ComponentModel.DataAnnotations;

namespace KnowledgeAssistant.Infrastructure.Search;

/// <summary>
/// Configuration for the Azure AI Search adapter, bound from the
/// <c>Azure:Search</c> section.
/// </summary>
/// <remarks>
/// <para>
/// <b>No API key.</b> Azure AI Search issues admin and query keys, and neither is
/// used here. Authentication is Entra ID via <c>DefaultAzureCredential</c>, so
/// the only configuration needed is where the service is and which index to
/// write to — neither of which is a credential. The credential itself is
/// configured once in <c>Azure:Credential</c>, shared with every other Azure
/// client.
/// </para>
/// <para>
/// <b>SemanticConfigurationName is deliberately not bound.</b> It exists in
/// <c>appsettings.json</c> for a later sprint. Binding a setting this slice does
/// not honour would imply a capability that is not implemented.
/// </para>
/// </remarks>
public sealed class AzureSearchOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Azure:Search";

    /// <summary>
    /// The search service endpoint, for example
    /// <c>https://contoso.search.windows.net/</c>.
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Azure:Search:Endpoint must be configured.")]
    [Url(ErrorMessage = "Azure:Search:Endpoint must be an absolute URL.")]
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>The index document metadata is written to.</summary>
    /// <remarks>
    /// The pattern is Azure AI Search's own index naming rule — 2 to 128
    /// characters of lowercase letters, digits, and dashes, not starting or
    /// ending with a dash. Validating it here turns a typo from a 400 on the
    /// first upload into a startup failure that names the setting.
    /// </remarks>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Azure:Search:IndexName must be configured.")]
    [RegularExpression(
        "^[a-z0-9][a-z0-9-]{0,126}[a-z0-9]$",
        ErrorMessage = "Azure:Search:IndexName must be 2-128 characters of lowercase letters, digits, and dashes, and may not start or end with a dash.")]
    public string IndexName { get; init; } = string.Empty;
}
