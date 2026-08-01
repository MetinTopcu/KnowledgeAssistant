using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace KnowledgeAssistant.Api.Extensions;

/// <summary>
/// Registers the HTTP delivery concerns, keeping <c>Program.cs</c> declarative.
/// </summary>
public static class ApiServiceCollectionExtensions
{
    /// <summary>Adds controllers and this API's error conventions.</summary>
    public static IServiceCollection AddApiServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddControllers();
        services.AddProblemDetails();

        // Model binding can fail before a request ever reaches a handler — a
        // malformed multipart body, or one Kestrel refused to finish reading. By
        // default [ApiController] answers those with its own 400 in a shape that
        // differs from the Result pipeline's, so a client would see two error
        // formats from one endpoint. This routes both through the same builder.
        services.Configure<ApiBehaviorOptions>(options =>
            options.InvalidModelStateResponseFactory = CreateModelStateResponse);

        return services;
    }

    private static ObjectResult CreateModelStateResponse(ActionContext context)
    {
        int statusCode = ResolveStatusCode(context.ModelState);

        Dictionary<string, string[]> errors = context.ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => string.IsNullOrEmpty(entry.Key) ? "Request" : entry.Key,
                entry => entry.Value!.Errors.Select(DescribeError).ToArray(),
                StringComparer.Ordinal);

        ValidationProblemDetails problemDetails =
            ApiProblemDetails.CreateValidation(errors, statusCode);

        return new ObjectResult(problemDetails)
        {
            StatusCode = statusCode,
            ContentTypes = { "application/problem+json" },
        };
    }

    /// <summary>
    /// Recovers the true status code for a body the server refused to read.
    /// </summary>
    /// <remarks>
    /// When a request exceeds the endpoint's size limit, Kestrel raises a
    /// <see cref="BadHttpRequestException"/> carrying status 413. MVC catches it
    /// while reading the form and files it as an ordinary model error, which
    /// would otherwise surface as a misleading 400 — telling the client its
    /// request was malformed when in fact it was simply too big, and hiding the
    /// one thing that would let them fix it.
    /// </remarks>
    private static int ResolveStatusCode(ModelStateDictionary modelState) =>
        modelState.Values
            .SelectMany(entry => entry.Errors)
            .Select(error => error.Exception)
            .OfType<BadHttpRequestException>()
            .Select(exception => exception.StatusCode)
            .DefaultIfEmpty(StatusCodes.Status400BadRequest)
            .Max();

    /// <summary>
    /// Prefers the framework's message, falling back to the exception's only when
    /// no message was supplied. Raw exception text is never returned to a client.
    /// </summary>
    private static string DescribeError(ModelError error) =>
        string.IsNullOrWhiteSpace(error.ErrorMessage)
            ? "The request could not be processed."
            : error.ErrorMessage;
}
