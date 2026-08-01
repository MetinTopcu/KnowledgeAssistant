using KnowledgeAssistant.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeAssistant.Api.Extensions;

/// <summary>
/// Translates a <see cref="Result{TValue}"/> into an HTTP response.
/// </summary>
/// <remarks>
/// <para>
/// This is the single place where a domain outcome becomes a status code. Doing
/// it once, here, is what keeps controllers free of <c>if (result.IsFailure)
/// return BadRequest(...)</c> and guarantees that two endpoints cannot map the
/// same <see cref="ErrorType"/> to different codes.
/// </para>
/// <para>
/// Failures are rendered as RFC 9457 <c>application/problem+json</c> — the
/// standard error format ASP.NET Core, Azure API Management, and most client
/// SDKs already understand, so callers get machine-readable failures without a
/// bespoke contract to learn.
/// </para>
/// </remarks>
internal static class ResultExtensions
{
    /// <summary>
    /// Returns <c>200 OK</c> with the value on success, or a problem response
    /// describing the failure.
    /// </summary>
    public static IActionResult ToActionResult<TValue>(this Result<TValue> result) =>
        result.IsSuccess
            ? new OkObjectResult(result.Value)
            : ToProblemResult(result.Error);

    private static ObjectResult ToProblemResult(Error error)
    {
        ProblemDetails problemDetails = error switch
        {
            ValidationError validationError => CreateValidationProblem(validationError),
            _ => CreateProblem(error),
        };

        ApiProblemDetails.Stamp(problemDetails);

        return new ObjectResult(problemDetails)
        {
            StatusCode = problemDetails.Status,
            ContentTypes = { "application/problem+json" },
        };
    }

    private static ValidationProblemDetails CreateValidationProblem(ValidationError validationError)
    {
        // Grouped by error code rather than by property name. The code is the
        // stable contract; property names are an implementation detail of the
        // command and would churn the client's error handling if renamed.
        Dictionary<string, string[]> failures = validationError.Errors
            .GroupBy(error => error.Code)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.Description).ToArray(),
                StringComparer.Ordinal);

        return ApiProblemDetails.CreateValidation(failures, StatusCodes.Status400BadRequest);
    }

    private static ProblemDetails CreateProblem(Error error) => new()
    {
        Status = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError,
        },
        Title = error.Code,
        Detail = error.Description,
    };
}
