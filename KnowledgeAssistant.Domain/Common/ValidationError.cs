namespace KnowledgeAssistant.Domain.Common;

/// <summary>
/// An <see cref="Error"/> that carries several individual failures.
/// </summary>
/// <remarks>
/// Input validation is the one case where returning the *first* failure is a
/// poor API: a caller who uploads a 30 MB <c>.docx</c> should be told both
/// things at once rather than discovering the second problem only after fixing
/// the first. This type is what lets a single <see cref="Result"/> carry the
/// full set.
/// </remarks>
public sealed record ValidationError : Error
{
    /// <summary>Creates a composite error from the individual failures.</summary>
    public ValidationError(IReadOnlyList<Error> errors)
        : base("Validation.General", "One or more validation errors occurred.", ErrorType.Validation)
    {
        Errors = errors;
    }

    /// <summary>The individual failures, each with its own code and description.</summary>
    public IReadOnlyList<Error> Errors { get; }
}
