using FluentValidation;
using FluentValidation.Results;
using KnowledgeAssistant.Application.Abstractions;
using KnowledgeAssistant.Application.Common;
using KnowledgeAssistant.Application.Interfaces;
using KnowledgeAssistant.Domain.Common;
using Microsoft.Extensions.Logging;

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
/// <remarks>
/// <para>
/// <b>Why the two steps are not atomic, and why that is the right trade.</b>
/// Sprint 4 makes the use case span two external systems, and no transaction
/// spans Blob Storage and AI Search. A failure between them therefore leaves the
/// bytes stored with no index entry.
/// </para>
/// <para>
/// That direction of inconsistency is chosen deliberately. The index is a
/// derived, rebuildable projection — a query accelerator, not the state of
/// record — so a stored blob missing from the index can always be re-indexed
/// from storage. The reverse, an index entry pointing at bytes that were never
/// written, is unrecoverable and would surface to users as a search hit that
/// 404s. When the two cannot be made consistent, fail towards the one that can
/// be repaired.
/// </para>
/// <para>
/// The orphan is logged with its blob name so a reconciliation pass can find it.
/// Deleting the blob to compensate was rejected: it would add a destructive
/// operation to the storage port, and a delete that itself fails leaves the same
/// problem plus a partially-executed rollback.
/// </para>
/// </remarks>
internal sealed partial class UploadDocumentCommandHandler
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
    private readonly IAzureSearchService _searchService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<UploadDocumentCommandHandler> _logger;

    /// <summary>Initialises the handler.</summary>
    public UploadDocumentCommandHandler(
        IValidator<UploadDocumentCommand> validator,
        IBlobStorageService blobStorageService,
        IAzureSearchService searchService,
        TimeProvider timeProvider,
        ILogger<UploadDocumentCommandHandler> logger)
    {
        _validator = validator;
        _blobStorageService = blobStorageService;
        _searchService = searchService;
        _timeProvider = timeProvider;
        _logger = logger;
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

        // Read once and used for both the indexed value and the response, so the
        // two can never disagree about when this document was accepted. Two reads
        // of the clock would differ by however long indexing took.
        DateTimeOffset receivedAtUtc = _timeProvider.GetUtcNow();

        Result indexResult = await _searchService
            .IndexDocumentAsync(
                new DocumentIndexRequest(
                    DocumentId: documentId,
                    BlobName: uploadResult.Value.BlobName,
                    OriginalFileName: request.FileName,
                    BlobUri: uploadResult.Value.BlobUri,
                    UploadedAt: receivedAtUtc),
                cancellationToken)
            .ConfigureAwait(false);

        if (indexResult.IsFailure)
        {
            // The upload is reported as failed because the document is not fully
            // ingested — it cannot be found. The blob survives, so this line is
            // what makes it recoverable rather than lost.
            LogIndexingLeftOrphanedBlob(documentId, uploadResult.Value.BlobName, indexResult.Error.Code);

            return Result.Failure<UploadDocumentResponse>(indexResult.Error);
        }

        return new UploadDocumentResponse(
            DocumentId: documentId,
            FileName: request.FileName,
            ContentType: request.ContentType,
            // The measured size from storage, not the client's declared length.
            SizeInBytes: uploadResult.Value.SizeInBytes,
            BlobName: uploadResult.Value.BlobName,
            ReceivedAtUtc: receivedAtUtc);
    }

    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Error,
        Message = "Document {DocumentId} was stored as {BlobName} but indexing failed with {ErrorCode}. " +
                  "The blob is orphaned and must be re-indexed or removed.")]
    private partial void LogIndexingLeftOrphanedBlob(Guid documentId, string blobName, string errorCode);
}
