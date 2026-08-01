using KnowledgeAssistant.Infrastructure.Search.Chunking;

namespace KnowledgeAssistant.Tests.Unit.Chunking;

/// <summary>
/// Chunk identifiers: derived, stable, and well-formed UUIDs.
/// </summary>
/// <remarks>
/// The determinism assertion is the load-bearing one. Re-processing a document
/// must reuse the same chunk ids, or the re-index writes a second complete set
/// and orphans the first — still matching queries, identifiable only by ids
/// nobody recorded.
/// </remarks>
public sealed class ChunkIdFactoryTests
{
    private static readonly Guid DocumentId = Guid.Parse("0195a1f0-1234-7abc-8def-0123456789ab");

    [Fact]
    public void Create_IsDeterministic()
    {
        ChunkIdFactory.Create(DocumentId, 0).Should().Be(ChunkIdFactory.Create(DocumentId, 0));
    }

    [Fact]
    public void Create_VariesWithChunkOrder()
    {
        ChunkIdFactory.Create(DocumentId, 0).Should().NotBe(ChunkIdFactory.Create(DocumentId, 1));
    }

    [Fact]
    public void Create_VariesWithDocument()
    {
        ChunkIdFactory.Create(DocumentId, 0).Should().NotBe(ChunkIdFactory.Create(Guid.CreateVersion7(), 0));
    }

    [Fact]
    public void Create_SetsTheRfc9562VersionAndVariantBits()
    {
        byte[] bytes = ChunkIdFactory.Create(DocumentId, 7).ToByteArray(bigEndian: true);

        (bytes[6] >> 4).Should().Be(8, "RFC 9562 version 8 is the implementation-defined form");
        (bytes[8] & 0xC0).Should().Be(0x80, "the variant must be the RFC 4122/9562 variant");
    }

    [Fact]
    public void Create_DoesNotCollideAcrossManyChunks()
    {
        HashSet<Guid> ids = [.. Enumerable.Range(0, 20_000).Select(order => ChunkIdFactory.Create(DocumentId, order))];

        ids.Should().HaveCount(20_000);
    }
}
