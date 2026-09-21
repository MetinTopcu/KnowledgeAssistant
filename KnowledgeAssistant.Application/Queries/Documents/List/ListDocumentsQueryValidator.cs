using FluentValidation;

namespace KnowledgeAssistant.Application.Queries.Documents.List;

/// <summary>
/// The acceptance rules for a corpus listing.
/// </summary>
/// <remarks>
/// One rule, and it is a resource guard rather than a formatting preference: the
/// limit is passed straight to the search service as a page size, so without an
/// upper bound a caller could ask for a response large enough to hurt this
/// process and the index behind it. Every rule carries an explicit error code, as
/// elsewhere, so a client can branch on the failure rather than on its wording.
/// </remarks>
internal sealed class ListDocumentsQueryValidator : AbstractValidator<ListDocumentsQuery>
{
    /// <summary>The largest page a caller may request.</summary>
    /// <remarks>
    /// Comfortably more than a screenful and far below anything that would strain
    /// a response. A caller that needs the whole corpus needs paging, which is a
    /// deliberate decision rather than a larger number here.
    /// </remarks>
    internal const int MaxResultsCeiling = 200;

    /// <summary>Initialises the rule set.</summary>
    public ListDocumentsQueryValidator()
    {
        RuleFor(query => query.MaxResults)
            .GreaterThan(0)
            .WithErrorCode("MaxResults.OutOfRange")
            .WithMessage("maxResults must be greater than zero.")
            .LessThanOrEqualTo(MaxResultsCeiling)
            .WithErrorCode("MaxResults.OutOfRange")
            .WithMessage($"maxResults must not exceed {MaxResultsCeiling}.");
    }
}
