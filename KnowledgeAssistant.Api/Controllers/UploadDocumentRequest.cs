namespace KnowledgeAssistant.Api.Controllers;

/// <summary>
/// The <c>multipart/form-data</c> body of <c>POST /api/documents</c>.
/// </summary>
/// <remarks>
/// <para>
/// This type lives in the API project, not in Application, because
/// <see cref="IFormFile"/> is an ASP.NET Core abstraction. It is the transport
/// shape; <c>UploadDocumentCommand</c> is the use-case shape. Keeping them
/// separate is what allows the same use case to be driven later by a queue
/// trigger without an HTTP type in sight.
/// </para>
/// <para>
/// <b>Why <see cref="File"/> is nullable.</b> With <c>[ApiController]</c>, a
/// non-nullable reference property makes model binding emit its own
/// <c>ModelState</c> error and short-circuit to a 400 before the request ever
/// reaches the handler. That response would bypass the Result pipeline and use a
/// different error shape from every other failure this endpoint returns.
/// Declaring it nullable lets the request through so
/// <c>UploadDocumentCommandValidator</c> reports the missing file in the same
/// format as every other rule.
/// </para>
/// <para>
/// It is a class with an init-only property rather than a record: positional
/// record parameters bind unreliably from multipart form data, and the failure
/// mode is a silently null file rather than a compile error.
/// </para>
/// </remarks>
public sealed class UploadDocumentRequest
{
    /// <summary>The PDF to upload, sent in the <c>file</c> form field.</summary>
    public IFormFile? File { get; init; }
}
