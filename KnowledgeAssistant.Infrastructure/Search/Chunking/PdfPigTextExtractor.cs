using System.Text;
using KnowledgeAssistant.Domain.Common;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Exceptions;
using UglyToad.PdfPig.Fonts;

namespace KnowledgeAssistant.Infrastructure.Search.Chunking;

/// <summary>
/// Extracts text from a PDF in-process using PdfPig.
/// </summary>
/// <remarks>
/// <para>
/// The extractor used when Azure Document Intelligence is not configured. It
/// costs nothing per document and needs no network, which makes local
/// development and testing possible without an Azure subscription. What it does
/// not do is OCR: a scanned page has no text layer, and this returns nothing for
/// it. That difference in capability is the reason the choice between extractors
/// is a deployment decision rather than a silent fallback.
/// </para>
/// <para>
/// <b>Registered as a singleton</b> and safe to be one: it holds no state between
/// calls, and every object it touches is local to a single extraction.
/// </para>
/// </remarks>
internal sealed partial class PdfPigTextExtractor : IPdfTextExtractor
{
    private readonly ILogger<PdfPigTextExtractor> _logger;

    /// <summary>Initialises the extractor.</summary>
    public PdfPigTextExtractor(ILogger<PdfPigTextExtractor> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<string>> ExtractTextAsync(Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        // PdfPig reads the cross-reference table at the end of the file and then
        // seeks backwards, so it cannot work from a forward-only stream. Buffering
        // is bounded by the endpoint's 20 MB request limit, which is what makes it
        // safe to do unconditionally here.
        Stream seekable = content;
        MemoryStream? buffer = null;

        try
        {
            if (!content.CanSeek)
            {
                buffer = new MemoryStream();
                await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
                buffer.Position = 0;
                seekable = buffer;
            }

            return ExtractText(seekable, cancellationToken);
        }
        finally
        {
            if (buffer is not null)
            {
                await buffer.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Performs the extraction synchronously.
    /// </summary>
    /// <remarks>
    /// Deliberately not wrapped in <c>Task.Run</c>. Parsing is CPU-bound work
    /// already running on a thread-pool thread; moving it to a different
    /// thread-pool thread would add a context switch and an allocation while
    /// freeing nothing. The caller's thread is going to wait either way.
    /// </remarks>
    private Result<string> ExtractText(Stream content, CancellationToken cancellationToken)
    {
        try
        {
            using PdfDocument document = PdfDocument.Open(
                content,
                // A single unusable embedded font should not cost the whole
                // document; real-world PDFs carry broken font resources routinely.
                new ParsingOptions { SkipMissingFonts = true });

            var text = new StringBuilder();

            foreach (Page page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();

                // addDoubleNewline makes the extractor emit a blank line where it
                // judges a new paragraph to begin. That is precisely the signal the
                // chunker looks for, so paragraph preservation starts here rather
                // than being reverse-engineered from the text afterwards.
                string pageText = ContentOrderTextExtractor.GetText(page, addDoubleNewline: true);

                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    text.Append(pageText);

                    // Page boundaries are treated as paragraph boundaries: a chunk
                    // spanning two pages should at least prefer to break where the
                    // page did.
                    text.Append("\n\n");
                }
            }

            return text.ToString();
        }
        catch (PdfDocumentEncryptedException exception)
        {
            LogDocumentPasswordProtected(exception);
            return Result.Failure<string>(ChunkingErrors.DocumentPasswordProtected);
        }
        catch (PdfDocumentFormatException exception)
        {
            LogDocumentUnreadable(exception);
            return Result.Failure<string>(ChunkingErrors.DocumentUnreadable);
        }
        catch (PdfDocumentStackDepthException exception)
        {
            // Deeply nested content streams: malformed, or crafted to exhaust the
            // stack. Either way the document is not one this service will process.
            LogDocumentUnreadable(exception);
            return Result.Failure<string>(ChunkingErrors.DocumentUnreadable);
        }
        catch (InvalidFontFormatException exception)
        {
            LogDocumentUnreadable(exception);
            return Result.Failure<string>(ChunkingErrors.DocumentUnreadable);
        }
        catch (CorruptCompressedDataException exception)
        {
            LogDocumentUnreadable(exception);
            return Result.Failure<string>(ChunkingErrors.DocumentUnreadable);
        }
    }

    [LoggerMessage(
        EventId = 4000,
        Level = LogLevel.Warning,
        Message = "A document could not be parsed as a PDF.")]
    private partial void LogDocumentUnreadable(Exception exception);

    [LoggerMessage(
        EventId = 4001,
        Level = LogLevel.Warning,
        Message = "A document is password protected and cannot be parsed.")]
    private partial void LogDocumentPasswordProtected(Exception exception);
}
