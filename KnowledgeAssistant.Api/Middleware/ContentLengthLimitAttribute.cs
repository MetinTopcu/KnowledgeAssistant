using KnowledgeAssistant.Api.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace KnowledgeAssistant.Api.Middleware;

/// <summary>
/// Rejects a request whose declared <c>Content-Length</c> exceeds a limit,
/// answering <c>413 Content Too Large</c> before the body is read.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists alongside <see cref="RequestSizeLimitAttribute"/>.</b>
/// <c>[RequestSizeLimit]</c> makes Kestrel abort an oversized upload mid-stream,
/// which is the protection that matters — but MVC catches the resulting
/// <see cref="BadHttpRequestException"/> while reading the form and records it
/// as an ordinary model error. The status that reaches the client is therefore
/// <c>400</c>, which tells them their request was malformed when it was merely
/// too big. The distinction is not pedantic: a 413 tells a client to send a
/// smaller file, while a 400 sends them hunting for a syntax error that is not
/// there.
/// </para>
/// <para>
/// A resource filter runs before model binding, so checking the declared length
/// here short-circuits with the correct status and never reads a byte of the
/// body.
/// </para>
/// <para>
/// <b>Both are required.</b> <c>Content-Length</c> is client-supplied and absent
/// on chunked uploads, so this filter alone could be bypassed. When it is
/// missing, <c>[RequestSizeLimit]</c> remains the enforcement that cannot be
/// lied to. This filter improves the error; that attribute provides the
/// guarantee.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class ContentLengthLimitAttribute : Attribute, IResourceFilter
{
    private readonly long _maxBytes;

    /// <summary>Creates the filter.</summary>
    /// <param name="maxBytes">The largest declared body length accepted.</param>
    public ContentLengthLimitAttribute(long maxBytes)
    {
        _maxBytes = maxBytes;
    }

    /// <summary>Short-circuits with 413 when the declared length is too large.</summary>
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        long? contentLength = context.HttpContext.Request.ContentLength;

        if (contentLength is null || contentLength <= _maxBytes)
        {
            return;
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Request"] =
            [
                $"The request body is {contentLength} bytes, which exceeds the maximum of {_maxBytes} bytes.",
            ],
        };

        ValidationProblemDetails problemDetails = ApiProblemDetails.CreateValidation(
            errors,
            StatusCodes.Status413PayloadTooLarge);

        context.Result = new ObjectResult(problemDetails)
        {
            StatusCode = StatusCodes.Status413PayloadTooLarge,
            ContentTypes = { "application/problem+json" },
        };
    }

    /// <summary>No post-execution work is required.</summary>
    public void OnResourceExecuted(ResourceExecutedContext context)
    {
        // Intentionally empty: the decision is made before the action runs.
    }
}
