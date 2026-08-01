using KnowledgeAssistant.Api.Extensions;
using KnowledgeAssistant.Api.Middleware;
using KnowledgeAssistant.Application.Commands.Documents.Upload;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeAssistant.Api.Controllers;

/// <summary>
/// Document ingestion endpoints.
/// </summary>
// No class-level [Produces("application/json")]. That attribute is a result
// filter which CLEARS ObjectResult.ContentTypes and substitutes its own list, so
// it silently overrode the "application/problem+json" that ResultExtensions and
// ApiProblemDetails set on every failure — leaving this API claiming RFC 9457
// while serving problem documents as ordinary application/json. The
// [ProducesResponseType] attributes below still describe the contract for
// OpenAPI, and the JSON formatter still produces application/json on success.
[ApiController]
[Route("api/documents")]
public sealed class DocumentsController : ControllerBase
{
    /// <summary>
    /// The largest request body Kestrel will buffer for this endpoint.
    /// </summary>
    /// <remarks>
    /// Slightly above the 20 MB file limit to leave room for multipart boundaries
    /// and headers, so a file at exactly the limit is rejected by the validator
    /// with a clear message rather than by the server with a bare 413.
    /// <para>
    /// This limit and the validator's are not redundant. This one is a resource
    /// guard: it makes Kestrel abort an oversized upload as it streams, so a
    /// caller cannot force the server to buffer a 2 GB body before any of our
    /// code runs. The validator's limit is the business rule that produces a
    /// helpful error. Removing either one leaves a real gap.
    /// </para>
    /// </remarks>
    private const long MaxRequestBodyBytes = 21L * 1024 * 1024;

    private readonly ISender _sender;

    /// <summary>Initialises the controller.</summary>
    public DocumentsController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// Uploads a PDF document for ingestion.
    /// </summary>
    /// <param name="request">The multipart form containing the <c>file</c> field.</param>
    /// <param name="cancellationToken">Cancelled when the client disconnects.</param>
    /// <returns>The accepted document's details, or a problem response.</returns>
    /// <remarks>
    /// The action is deliberately mechanical: map transport to command, send,
    /// translate the outcome. It holds no rule about what a valid document is —
    /// asking "is this a PDF?" here would put a business rule somewhere no
    /// non-HTTP caller could reach it.
    /// <para>
    /// <c>ISender</c> is injected rather than <c>IMediator</c> because this
    /// endpoint only sends requests. <c>IMediator</c> additionally exposes
    /// publish, which a controller has no business calling — Interface
    /// Segregation applied to the mediator itself.
    /// </para>
    /// </remarks>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [ContentLengthLimit(MaxRequestBodyBytes)]
    [ProducesResponseType(typeof(UploadDocumentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> UploadAsync(
        [FromForm] UploadDocumentRequest request,
        CancellationToken cancellationToken)
    {
        IFormFile? file = request.File;

        // An absent file is mapped to an empty command rather than short-circuited
        // here, so that "no file supplied" is reported by the validator in the same
        // shape as every other rule instead of via a second, divergent error path.
        //
        // Path.GetFileName strips any directory component: IFormFile.FileName is
        // attacker-controlled, and some clients legitimately send a full client-side
        // path. Sanitising at the boundary means no inner layer has to remember that
        // this value is hostile, and the name echoed back in the response cannot
        // carry a traversal sequence.
        //
        // The stream is opened here and disposed here. Stream.Null stands in when
        // no file was sent: the validator rejects that command before the handler
        // reaches storage, so the placeholder is never uploaded. Opening the
        // stream before validation is cheap — it is a view over the request body
        // ASP.NET Core has already buffered, not a copy.
        await using Stream content = file?.OpenReadStream() ?? Stream.Null;

        var command = new UploadDocumentCommand(
            FileName: file is null ? string.Empty : Path.GetFileName(file.FileName),
            ContentType: file?.ContentType ?? string.Empty,
            SizeInBytes: file?.Length ?? 0,
            Content: content);

        var result = await _sender.Send(command, cancellationToken).ConfigureAwait(false);

        return result.ToActionResult();
    }
}
