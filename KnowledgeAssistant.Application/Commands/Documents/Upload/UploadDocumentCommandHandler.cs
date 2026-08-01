using FluentValidation;
using FluentValidation.Results;
using KnowledgeAssistant.Application.Abstractions;
using KnowledgeAssistant.Application.Common;
using KnowledgeAssistant.Application.Interfaces;
using KnowledgeAssistant.Domain.Common;

namespace KnowledgeAssistant.Application.Commands.Documents.Upload;

/// <summary>
/// Validates an upload request and confirms acceptance.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why validation is invoked here rather than by a pipeline behaviour.</b>
/// A <c>ValidationBehavior</c> is the right destination and the approved
/// architecture reserves a folder for it — but building a generic,
/// <c>Result</c>-returning behaviour requires reflection to construct
/// <c>Result&lt;T&gt;.Failure</c> for an open <c>TResponse</c>, and that
/// machinery cannot be justified by a single slice. Vertical Slice Architecture
/// argues the same way: keep the slice self-contained and extract the shared
/// abstraction when a second and third slice show what it actually needs to be.
/// The moment these six lines appear in a third handler, promote them — the
/// handlers lose the <c>IValidator</c> dependency and nothing else changes.
/// </para>
/// <para>
/// <b>Why <see cref="TimeProvider"/> rather than <c>DateTimeOffset.UtcNow</c>.</b>
/// A static clock read is untestable — an assertion on
/// <c>ReceivedAtUtc</c> would have to allow a tolerance window and would still
/// flake. <see cref="TimeProvider"/> is the BCL abstraction for this since
/// .NET 8, so no hand-rolled <c>IDateTimeProvider</c> port is needed, and tests
/// substitute <c>FakeTimeProvider</c> for an exact value.
/// </para>
/// <para>
/// <b>Why the class is <c>internal</c>.</b> Nothing outside this assembly should
/// construct or call a handler directly; the only legitimate entry point is
/// sending the command through the mediator. MediatR still discovers it, because
/// assembly scanning sees internal types.
/// </para>
/// </remarks>
internal sealed class UploadDocumentCommandHandler
    : ICommandHandler<UploadDocumentCommand, UploadDocumentResponse>
{
    /// <summary>
    /// The content type recorded against the stored blob.
    /// </summary>
    /// <remarks>
    /// Deliberately not the client's declared <c>ContentType</c>. That value is
    /// attacker-controlled, and persisting it means whatever the caller sent is
    /// what the service will later hand back in a <c>Content-Type</c> header — a
    /// stored cross-site-scripting vector the day a download endpoint exists.
    /// The extension has been validated as <c>.pdf</c>, so PDF is the only type
    /// this system is willing to serve the bytes as.
    /// <para>
    /// The response still echoes what the client declared, because that is a
    /// faithful report of the request. This constant is what was stored.
    /// </para>
    /// </remarks>
    private const string StoredContentType = "application/pdf";

    private readonly IValidator<UploadDocumentCommand> _validator;
    private readonly IBlobStorageService _blobStorageService;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initialises the handler.</summary>
    public UploadDocumentCommandHandler(
        IValidator<UploadDocumentCommand> validator,
        IBlobStorageService blobStorageService,
        TimeProvider timeProvider)
    {
        _validator = validator;
        _blobStorageService = blobStorageService;
        _timeProvider = timeProvider;
    }

    /// <summary>Validates the command and returns the acceptance confirmation.</summary>
    public async Task<Result<UploadDocumentResponse>> Handle(
        UploadDocumentCommand request,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator
            .ValidateAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (!validationResult.IsValid)
        {
            return Result.Failure<UploadDocumentResponse>(validationResult.ToValidationError());
        }

        // Version 7 GUIDs embed a timestamp in their high bits, so they sort in
        // creation order. When this identifier becomes a database key in the next
        // slice, that ordering is the difference between sequential inserts and
        // the index-page fragmentation that random v4 GUIDs cause at volume.
        // It is generated before the upload because the blob name derives from it.
        var documentId = Guid.CreateVersion7();

        Result<BlobUploadResult> uploadResult = await _blobStorageService
            .UploadAsync(
                documentId,
                request.FileName,
                StoredContentType,
                request.Content,
                cancellationToken)
            .ConfigureAwait(false);

        if (uploadResult.IsFailure)
        {
            return Result.Failure<UploadDocumentResponse>(uploadResult.Error);
        }

        return new UploadDocumentResponse(
            DocumentId: documentId,
            FileName: request.FileName,
            ContentType: request.ContentType,
            // The measured size from storage, not the client's declared length.
            SizeInBytes: uploadResult.Value.SizeInBytes,
            BlobName: uploadResult.Value.BlobName,
            ReceivedAtUtc: _timeProvider.GetUtcNow());
    }
}
