using FluentValidation;
using FluentValidation.Results;
using KnowledgeAssistant.Application.Abstractions;
using KnowledgeAssistant.Application.Common;
using KnowledgeAssistant.Application.Interfaces;
using KnowledgeAssistant.Domain.Common;
using Microsoft.Extensions.Logging;

namespace KnowledgeAssistant.Application.Queries.Documents.List;

/// <summary>
/// Lists the ingested corpus.
/// </summary>
/// <remarks>
/// <para>
/// The whole slice is validate, call one port, map. There is no second stage to
/// fail between, which makes this the one read path in the service with nothing
/// to reconcile and nothing to explain when it goes wrong.
/// </para>
/// <para>
/// <b>An empty corpus is a success.</b> Same rule as retrieval: "nothing has been
/// uploaded" is an answer, and a caller rendering a list has an obvious response
/// to it. The port already treats a missing index the same way, so this handler
/// has no special case for a system that has never ingested anything.
/// </para>
/// <para>
/// <b>Failures pass through unaltered</b>, so a client sees <c>Search.*</c> and
/// an operator can tell from the code alone which dependency was at fault.
/// </para>
/// </remarks>
internal sealed partial class ListDocumentsQueryHandler
    : IQueryHandler<ListDocumentsQuery, ListDocumentsResponse>
{
    private readonly IValidator<ListDocumentsQuery> _validator;
    private readonly IAzureSearchService _searchService;
    private readonly ILogger<ListDocumentsQueryHandler> _logger;

    /// <summary>Initialises the handler.</summary>
    public ListDocumentsQueryHandler(
        IValidator<ListDocumentsQuery> validator,
        IAzureSearchService searchService,
        ILogger<ListDocumentsQueryHandler> logger)
    {
        _validator = validator;
        _searchService = searchService;
        _logger = logger;
    }

    /// <summary>Returns the indexed documents, newest first.</summary>
    public async Task<Result<ListDocumentsResponse>> Handle(
        ListDocumentsQuery request,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator
            .ValidateAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (!validationResult.IsValid)
        {
            return Result.Failure<ListDocumentsResponse>(validationResult.ToValidationError());
        }

        Result<IReadOnlyList<DocumentIndexEntry>> listing = await _searchService
            .ListDocumentsAsync(request.MaxResults, cancellationToken)
            .ConfigureAwait(false);

        if (listing.IsFailure)
        {
            LogListingFailed(listing.Error.Code);
            return Result.Failure<ListDocumentsResponse>(listing.Error);
        }

        IReadOnlyList<DocumentIndexEntry> documents = listing.Value;

        // File names are not logged: they are user-supplied and can carry the
        // subject of a private document in the name alone.
        LogDocumentsListed(documents.Count, request.MaxResults);

        var summaries = documents
            .Select(document => new DocumentSummary(
                DocumentId: document.DocumentId,
                FileName: document.OriginalFileName,
                BlobName: document.BlobName,
                UploadedAtUtc: document.UploadedAt))
            .ToArray();

        return new ListDocumentsResponse(
            Documents: summaries,
            Count: summaries.Length,
            Truncated: summaries.Length >= request.MaxResults);
    }

    [LoggerMessage(
        EventId = 7200,
        Level = LogLevel.Information,
        Message = "Listed {DocumentCount} documents (limit {MaxResults}).")]
    private partial void LogDocumentsListed(int documentCount, int maxResults);

    [LoggerMessage(
        EventId = 7201,
        Level = LogLevel.Warning,
        Message = "Listing the corpus failed with {ErrorCode}.")]
    private partial void LogListingFailed(string errorCode);
}
