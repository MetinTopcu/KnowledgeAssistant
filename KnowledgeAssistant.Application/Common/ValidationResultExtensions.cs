using FluentValidation.Results;
using KnowledgeAssistant.Domain.Common;

namespace KnowledgeAssistant.Application.Common;

/// <summary>
/// Bridges FluentValidation's result type to the Result pattern.
/// </summary>
/// <remarks>
/// This adapter is the reason no handler ever throws
/// <c>ValidationException</c>. FluentValidation's own
/// <c>.ValidateAndThrow()</c> would put an expected business outcome onto the
/// exception path — precisely what the Result pattern is here to avoid.
/// <para>
/// It lives in <c>Common/</c> rather than in the slice because every future
/// slice needs exactly this translation, and duplicating it would let error
/// codes drift between features.
/// </para>
/// </remarks>
internal static class ValidationResultExtensions
{
    /// <summary>
    /// Converts FluentValidation failures into a single <see cref="ValidationError"/>,
    /// preserving every failure so the caller sees all problems at once.
    /// </summary>
    public static ValidationError ToValidationError(this ValidationResult validationResult) =>
        new([.. validationResult.Errors.Select(failure =>
            Error.Validation(failure.ErrorCode, failure.ErrorMessage))]);
}
