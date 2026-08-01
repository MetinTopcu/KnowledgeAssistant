using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeAssistant.Api.Extensions;

/// <summary>
/// Builds the single error shape this API returns.
/// </summary>
/// <remarks>
/// Every failure — whether it came from a <c>Result</c>, from model binding, or
/// from Kestrel rejecting an oversized body — is rendered here. Without a shared
/// builder the API grows two dialects of error: the framework's and ours, with
/// different <c>type</c> URIs and different key conventions. A client would then
/// need two parsers for one endpoint.
/// </remarks>
internal static class ApiProblemDetails
{
    /// <summary>The RFC 9110 section describing 400 Bad Request.</summary>
    internal const string BadRequestType = "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.1";

    /// <summary>The RFC 9110 section describing 413 Content Too Large.</summary>
    internal const string ContentTooLargeType = "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.14";

    /// <summary>Creates a validation problem with this API's conventions applied.</summary>
    internal static ValidationProblemDetails CreateValidation(
        IDictionary<string, string[]> errors,
        int statusCode)
    {
        var problemDetails = new ValidationProblemDetails(errors)
        {
            Status = statusCode,
            Title = "One or more validation errors occurred.",
            Type = statusCode == StatusCodes.Status413PayloadTooLarge
                ? ContentTooLargeType
                : BadRequestType,
        };

        Stamp(problemDetails);
        return problemDetails;
    }

    /// <summary>
    /// Attaches the trace identifier that ties this response to the server logs.
    /// </summary>
    /// <remarks>
    /// <see cref="Activity.Current"/> carries the W3C <c>traceparent</c> when
    /// distributed tracing is active, so a support ticket quoting this value can
    /// be resolved to the exact request across services.
    /// </remarks>
    internal static void Stamp(ProblemDetails problemDetails)
    {
        problemDetails.Extensions["traceId"] = Activity.Current?.Id;
    }
}
