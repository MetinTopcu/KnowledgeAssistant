using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.Identity;
using KnowledgeAssistant.Domain.Common;
using KnowledgeAssistant.Infrastructure.Search.Chunking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAssistant.Infrastructure.Azure.DocumentIntelligence;

/// <summary>
/// Extracts text from a document using Azure Document Intelligence.
/// </summary>
/// <remarks>
/// <para>
/// Used when an endpoint is configured. It reads scanned pages that the local
/// extractor cannot, because the service performs OCR — which is the reason to
/// pay for it.
/// </para>
/// <para>
/// <b>Registered as a singleton.</b> <see cref="DocumentIntelligenceClient"/> is
/// thread-safe and holds a pooled <c>HttpClient</c> and a cached token, so a
/// per-request instance would re-run the credential chain and exhaust sockets
/// under load — the same reasoning as every other Azure adapter here.
/// </para>
/// <para>
/// <b>This class lives under <c>Azure/</c> deliberately</b>, even though it
/// implements a seam declared in <c>Search/Chunking</c>. That folder's rule is
/// that every reference to an Azure SDK type is confined to it, so that an SDK
/// upgrade is a diff in one place. The interface it satisfies carries no Azure
/// type, so the dependency points the harmless way.
/// </para>
/// </remarks>
internal sealed partial class DocumentIntelligenceTextExtractor : IPdfTextExtractor
{
    private readonly DocumentIntelligenceClient _client;
    private readonly string _modelId;
    private readonly ILogger<DocumentIntelligenceTextExtractor> _logger;

    /// <summary>Initialises the extractor.</summary>
    public DocumentIntelligenceTextExtractor(
        DocumentIntelligenceClient client,
        IOptions<DocumentIntelligenceOptions> options,
        ILogger<DocumentIntelligenceTextExtractor> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);

        _client = client;
        _modelId = options.Value.ModelId;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<string>> ExtractTextAsync(Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        try
        {
            // The whole document is buffered because the service takes a complete
            // body, not a stream. Bounded by the endpoint's 20 MB request limit.
            BinaryData document = await BinaryData
                .FromStreamAsync(content, cancellationToken)
                .ConfigureAwait(false);

            // WaitUntil.Completed: analysis is a long-running operation, and the
            // SDK polls it to completion. The caller is waiting synchronously for
            // an ingestion result, so there is nothing useful to do with an
            // operation handle that has not finished.
            Operation<AnalyzeResult> operation = await _client
                .AnalyzeDocumentAsync(WaitUntil.Completed, _modelId, document, cancellationToken)
                .ConfigureAwait(false);

            string text = operation.Value.Content ?? string.Empty;

            LogExtractionSucceeded(text.Length, operation.Value.Pages?.Count ?? 0);

            return text;
        }
        catch (RequestFailedException exception) when (exception.Status is 400 or 415)
        {
            // The service could not make sense of the bytes: unsupported format or
            // a corrupt file. That is a property of the caller's upload, not an
            // outage, and it is reported as such.
            LogDocumentRejected(exception, exception.Status, exception.ErrorCode);
            return Result.Failure<string>(ChunkingErrors.DocumentUnreadable);
        }
        catch (RequestFailedException exception)
        {
            LogExtractionFailed(exception, exception.Status, exception.ErrorCode);
            return Result.Failure<string>(ChunkingErrors.ExtractionServiceUnavailable);
        }
        catch (AuthenticationFailedException exception)
        {
            LogAuthenticationFailed(exception);
            return Result.Failure<string>(ChunkingErrors.ExtractionServiceUnavailable);
        }
        catch (AggregateException exception)
        {
            // Retries exhausted at the transport level arrive here rather than as
            // RequestFailedException — the same trap the blob and search adapters
            // guard against.
            LogTransportFailed(exception);
            return Result.Failure<string>(ChunkingErrors.ExtractionServiceUnavailable);
        }
    }

    [LoggerMessage(
        EventId = 4100,
        Level = LogLevel.Information,
        Message = "Document Intelligence extracted {CharacterCount} characters from {PageCount} pages.")]
    private partial void LogExtractionSucceeded(int characterCount, int pageCount);

    [LoggerMessage(
        EventId = 4101,
        Level = LogLevel.Warning,
        Message = "Document Intelligence rejected the document with {Status} ({ErrorCode}).")]
    private partial void LogDocumentRejected(Exception exception, int status, string? errorCode);

    [LoggerMessage(
        EventId = 4102,
        Level = LogLevel.Error,
        Message = "Document Intelligence failed with {Status} ({ErrorCode}).")]
    private partial void LogExtractionFailed(Exception exception, int status, string? errorCode);

    [LoggerMessage(
        EventId = 4103,
        Level = LogLevel.Error,
        Message = "Failed to authenticate to Azure Document Intelligence. Verify the managed identity and its RBAC role assignments.")]
    private partial void LogAuthenticationFailed(Exception exception);

    [LoggerMessage(
        EventId = 4104,
        Level = LogLevel.Error,
        Message = "Azure Document Intelligence was unreachable after all retries were exhausted.")]
    private partial void LogTransportFailed(Exception exception);
}
