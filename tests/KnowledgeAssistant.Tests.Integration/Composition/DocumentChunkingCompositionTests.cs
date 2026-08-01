using KnowledgeAssistant.Application.Interfaces;
using KnowledgeAssistant.Domain.Common;
using KnowledgeAssistant.Tests.Integration.Harness;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAssistant.Tests.Integration.Composition;

/// <summary>
/// Chunking a real PDF through the real container.
/// </summary>
/// <remarks>
/// <para>
/// The chunking service is resolved from the production composition root and
/// handed genuine PDF bytes, so this exercises the whole local extraction path:
/// the registration that chose <c>PdfPigTextExtractor</c>, the extractor itself,
/// the chunk-size and overlap settings as bound from configuration, and the id
/// derivation. A unit test of <c>TextChunker</c> covers the splitting algorithm;
/// nothing but this covers the assembly of those parts.
/// </para>
/// <para>
/// It needs no network, which is why it can live in the suite at all: Document
/// Intelligence is deliberately unconfigured, and that is the branch under test.
/// </para>
/// </remarks>
public sealed class DocumentChunkingCompositionTests
{
    private static IDocumentChunkingService Chunking(KnowledgeAssistantApiFactory factory) =>
        factory.Services.GetRequiredService<IDocumentChunkingService>();

    [Fact]
    public async Task Chunk_ARealPdf_ProducesOrderedNonEmptyChunksWithinTheConfiguredSize()
    {
        using var factory = new KnowledgeAssistantApiFactory(substituteAzureServices: false);
        using var content = new MemoryStream(TestContent.Pdf());

        Result<IReadOnlyList<DocumentChunk>> result =
            await Chunking(factory).ChunkAsync(Guid.CreateVersion7(), content, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        IReadOnlyList<DocumentChunk> chunks = result.Value;

        chunks.Should().HaveCountGreaterThan(1);
        chunks.Should().OnlyContain(chunk => chunk.Text.Length <= 800, "Chunking:MaxChunkSize is 800");
        chunks.Should().OnlyContain(chunk => !string.IsNullOrWhiteSpace(chunk.Text));
        chunks.Select(chunk => chunk.ChunkOrder).Should().Equal(Enumerable.Range(0, chunks.Count));
        chunks.Select(chunk => chunk.ChunkId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Chunk_ExtractsTextFromEveryPage()
    {
        using var factory = new KnowledgeAssistantApiFactory(substituteAzureServices: false);
        using var content = new MemoryStream(TestContent.Pdf(pages: 3));

        Result<IReadOnlyList<DocumentChunk>> result =
            await Chunking(factory).ChunkAsync(Guid.CreateVersion7(), content, CancellationToken.None);

        string all = string.Join(" ", result.Value.Select(chunk => chunk.Text));

        all.Should().Contain("Page1").And.Contain("Page2").And.Contain("Page3");
    }

    [Fact]
    public async Task Chunk_IsIdempotentForTheSameDocument()
    {
        // The claim the whole re-ingestion story rests on. If ids were random, a
        // re-index would write a second complete set of chunks and orphan the
        // first — still matching queries, identifiable only by ids nobody recorded.
        using var factory = new KnowledgeAssistantApiFactory(substituteAzureServices: false);
        byte[] pdf = TestContent.Pdf();
        var documentId = Guid.CreateVersion7();

        using var first = new MemoryStream(pdf);
        using var second = new MemoryStream(pdf);

        Result<IReadOnlyList<DocumentChunk>> before =
            await Chunking(factory).ChunkAsync(documentId, first, CancellationToken.None);
        Result<IReadOnlyList<DocumentChunk>> after =
            await Chunking(factory).ChunkAsync(documentId, second, CancellationToken.None);

        after.Value.Select(chunk => chunk.ChunkId)
            .Should().Equal(before.Value.Select(chunk => chunk.ChunkId));
    }

    [Fact]
    public async Task Chunk_DerivesDifferentIdsForADifferentDocument()
    {
        // The other half of determinism: identical text in two documents must not
        // collide, or one upload silently overwrites the other's chunks.
        using var factory = new KnowledgeAssistantApiFactory(substituteAzureServices: false);
        byte[] pdf = TestContent.Pdf();

        using var first = new MemoryStream(pdf);
        using var second = new MemoryStream(pdf);

        Result<IReadOnlyList<DocumentChunk>> one =
            await Chunking(factory).ChunkAsync(Guid.CreateVersion7(), first, CancellationToken.None);
        Result<IReadOnlyList<DocumentChunk>> other =
            await Chunking(factory).ChunkAsync(Guid.CreateVersion7(), second, CancellationToken.None);

        one.Value.Select(chunk => chunk.ChunkId)
            .Should().NotIntersectWith(other.Value.Select(chunk => chunk.ChunkId));
    }

    [Fact]
    public async Task Chunk_WithBytesThatAreNotAPdf_FailsAsACallerError()
    {
        // The error type decides the status code. Classified as a fault, an
        // unreadable upload would answer 500 and be paged on as an outage.
        using var factory = new KnowledgeAssistantApiFactory(substituteAzureServices: false);
        using var garbage = new MemoryStream("this is definitely not a pdf"u8.ToArray());

        Result<IReadOnlyList<DocumentChunk>> result =
            await Chunking(factory).ChunkAsync(Guid.CreateVersion7(), garbage, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task Chunk_WithATextFreePdf_FailsRatherThanReturningNoChunks()
    {
        // Returning zero chunks would let a document move through ingestion looking
        // healthy while contributing nothing an answer could ever cite.
        using var factory = new KnowledgeAssistantApiFactory(substituteAzureServices: false);
        using var blank = new MemoryStream(TestContent.TextFreePdf());

        Result<IReadOnlyList<DocumentChunk>> result =
            await Chunking(factory).ChunkAsync(Guid.CreateVersion7(), blank, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Chunking.NoTextExtracted");
    }

    [Fact]
    public async Task Chunk_AcceptsAForwardOnlyStream()
    {
        // PdfPig needs random access; an HTTP body does not provide it. The service
        // buffers, and this is the test that keeps that path working.
        using var factory = new KnowledgeAssistantApiFactory(substituteAzureServices: false);
        using var forwardOnly = new ForwardOnlyStream(new MemoryStream(TestContent.Pdf()));

        Result<IReadOnlyList<DocumentChunk>> result =
            await Chunking(factory).ChunkAsync(Guid.CreateVersion7(), forwardOnly, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    /// <summary>A read-only, non-seekable view over another stream.</summary>
    private sealed class ForwardOnlyStream(Stream inner) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
