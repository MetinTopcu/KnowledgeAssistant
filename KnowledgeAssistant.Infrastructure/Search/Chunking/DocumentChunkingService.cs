using KnowledgeAssistant.Application.Interfaces;
using KnowledgeAssistant.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAssistant.Infrastructure.Search.Chunking;

/// <summary>
/// Extracts a document's text and splits it into chunks.
/// </summary>
/// <remarks>
/// <para>
/// <b>This class orchestrates; it does not compute.</b> Extraction belongs to an
/// <see cref="IPdfTextExtractor"/> and splitting to <see cref="TextChunker"/>,
/// leaving this type with sequencing, identifier assignment, and the decision
/// that an empty document is a failure. That division is what makes the whole
/// slice testable: the chunking rules are exercised against string literals, and
/// this class against a stub extractor, with no PDF fixture needed for either.
/// </para>
/// <para>
/// <b>Registered as a singleton</b> and safe to be one: it holds only its
/// injected collaborators and a snapshot of the options, and nothing mutates
/// between calls.
/// </para>
/// </remarks>
internal sealed partial class DocumentChunkingService : IDocumentChunkingService
{
    private readonly IPdfTextExtractor _textExtractor;
    private readonly ChunkingOptions _options;
    private readonly ILogger<DocumentChunkingService> _logger;

    /// <summary>Initialises the service.</summary>
    public DocumentChunkingService(
        IPdfTextExtractor textExtractor,
        IOptions<ChunkingOptions> options,
        ILogger<DocumentChunkingService> logger)
    {
        ArgumentNullException.ThrowIfNull(textExtractor);
        ArgumentNullException.ThrowIfNull(options);

        _textExtractor = textExtractor;

        // Captured once. IOptions is already a singleton snapshot, so re-reading
        // .Value on every call would buy nothing; IOptionsMonitor would be the
        // type to use if these were ever meant to change at runtime, and they are
        // not — a change to chunk size invalidates every chunk already produced.
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<DocumentChunk>>> ChunkAsync(
        Guid documentId,
        Stream content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        Result<string> extraction = await _textExtractor
            .ExtractTextAsync(content, cancellationToken)
            .ConfigureAwait(false);

        if (extraction.IsFailure)
        {
            return Result.Failure<IReadOnlyList<DocumentChunk>>(extraction.Error);
        }

        IReadOnlyList<string> chunkTexts = TextChunker.Split(
            extraction.Value,
            _options.MaxChunkSize,
            _options.OverlapSize);

        // An empty result means the document parsed but held no text — a scan
        // without a text layer, almost always. Reporting success with zero chunks
        // would let it move through ingestion looking healthy while being
        // permanently unfindable by its contents.
        if (chunkTexts.Count == 0)
        {
            LogNoTextExtracted(documentId);
            return Result.Failure<IReadOnlyList<DocumentChunk>>(ChunkingErrors.NoTextExtracted);
        }

        var chunks = new DocumentChunk[chunkTexts.Count];

        for (int order = 0; order < chunkTexts.Count; order++)
        {
            chunks[order] = new DocumentChunk(
                ChunkIdFactory.Create(documentId, order),
                order,
                chunkTexts[order]);
        }

        LogChunked(documentId, chunks.Length, extraction.Value.Length);

        return Result.Success<IReadOnlyList<DocumentChunk>>(chunks);
    }

    [LoggerMessage(
        EventId = 4200,
        Level = LogLevel.Information,
        Message = "Split document {DocumentId} into {ChunkCount} chunks from {CharacterCount} characters.")]
    private partial void LogChunked(Guid documentId, int chunkCount, int characterCount);

    [LoggerMessage(
        EventId = 4201,
        Level = LogLevel.Warning,
        Message = "Document {DocumentId} yielded no extractable text.")]
    private partial void LogNoTextExtracted(Guid documentId);
}
