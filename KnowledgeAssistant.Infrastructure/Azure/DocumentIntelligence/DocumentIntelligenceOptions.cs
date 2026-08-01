using System.ComponentModel.DataAnnotations;

namespace KnowledgeAssistant.Infrastructure.Azure.DocumentIntelligence;

/// <summary>
/// Configuration for Azure Document Intelligence, bound from the
/// <c>Azure:DocumentIntelligence</c> section.
/// </summary>
/// <remarks>
/// <para>
/// <b>The endpoint is optional, and that is the whole feature.</b> Leaving it
/// empty selects the local PDF extractor instead. It is therefore the one
/// setting in this solution whose absence is a valid configuration rather than a
/// misconfiguration, which is why it carries no <c>[Required]</c> attribute
/// while every other endpoint does.
/// </para>
/// <para>
/// <b>No API key.</b> Document Intelligence issues keys; none is used.
/// Authentication is Entra ID via <c>DefaultAzureCredential</c>, sharing the
/// credential configured once in <c>Azure:Credential</c>. The data-plane role
/// required is <c>Cognitive Services User</c>.
/// </para>
/// </remarks>
public sealed class DocumentIntelligenceOptions : IValidatableObject
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Azure:DocumentIntelligence";

    /// <summary>
    /// The Document Intelligence endpoint, or empty to use local extraction.
    /// </summary>
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>The prebuilt model used to read documents.</summary>
    /// <remarks>
    /// <c>prebuilt-read</c> performs OCR and text extraction and nothing else,
    /// which is exactly the scope of this slice. The richer models
    /// (<c>prebuilt-layout</c>, <c>prebuilt-document</c>) additionally return
    /// tables, key-value pairs, and structure — all of which cost more per page
    /// and none of which anything here consumes.
    /// </remarks>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Azure:DocumentIntelligence:ModelId must be configured.")]
    public string ModelId { get; init; } = "prebuilt-read";

    /// <summary>
    /// Whether Document Intelligence should be used in place of local extraction.
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Endpoint);

    /// <summary>
    /// Validates the endpoint only when one has been supplied.
    /// </summary>
    /// <remarks>
    /// A conditional rule, so it cannot be an attribute: <c>[Url]</c> would
    /// reject the empty string that legitimately means "use local extraction",
    /// and omitting validation entirely would let a typo silently select an
    /// unreachable service.
    /// </remarks>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (IsConfigured && !Uri.TryCreate(Endpoint, UriKind.Absolute, out _))
        {
            yield return new ValidationResult(
                "Azure:DocumentIntelligence:Endpoint must be an absolute URL when it is set.",
                [nameof(Endpoint)]);
        }
    }
}
