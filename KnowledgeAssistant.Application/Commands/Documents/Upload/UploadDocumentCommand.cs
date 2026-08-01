using KnowledgeAssistant.Application.Abstractions;

namespace KnowledgeAssistant.Application.Commands.Documents.Upload;

/// <summary>
/// Requests that a document be accepted for ingestion.
/// </summary>
/// <param name="FileName">
/// The file name, already reduced to its leaf by the caller. See the remarks —
/// this must not contain a directory component.
/// </param>
/// <param name="ContentType">The MIME type declared by the client.</param>
/// <param name="SizeInBytes">The size of the uploaded content in bytes.</param>
/// <param name="Content">
/// The document's bytes. Read once by the handler and owned by the caller,
/// which is responsible for disposing it.
/// </param>
/// <remarks>
/// <para>
/// <b>Why there is no <c>IFormFile</c> here.</b> <c>IFormFile</c> lives in
/// <c>Microsoft.AspNetCore.Http</c>. Accepting one would force the Application
/// project to reference ASP.NET Core, which would end the layer's independence
/// from its delivery mechanism: the use case could then never be driven by a
/// queue trigger, a background worker, or a test without dragging a web
/// framework along. The command therefore carries the *facts about* the file,
/// and the controller does the translating.
/// </para>
/// <para>
/// <b>The content stream, added in Sprint 3.</b> Sprint 2 deliberately omitted
/// it because nothing read bytes; this is the slice that does. It is a
/// <see cref="Stream"/> rather than a <c>byte[]</c> so a 20 MB document is never
/// materialised on the large object heap — the bytes flow from the request
/// straight to storage.
/// </para>
/// <para>
/// <b>The consequence to be aware of.</b> A stream makes this command
/// non-serialisable and single-use, so it cannot be queued, replayed, or
/// retried after the request ends. That is correct for a synchronous upload. If
/// ingestion later needs durable retry, the shape changes rather than bends:
/// store the bytes first, then enqueue a command carrying the blob name instead
/// of the content.
/// </para>
/// <para>
/// <b>Trust boundary.</b> <c>ContentType</c> and <c>FileName</c> are supplied by
/// the client and are not evidence of anything. The extension check in the
/// validator is a cheap filter, not proof the payload is a PDF; confirming that
/// requires inspecting the content itself, which belongs in the slice that
/// opens the stream.
/// </para>
/// </remarks>
public sealed record UploadDocumentCommand(
    string FileName,
    string ContentType,
    long SizeInBytes,
    Stream Content) : ICommand<UploadDocumentResponse>;
