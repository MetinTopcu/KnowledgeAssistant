using System.Buffers.Binary;
using System.Security.Cryptography;

namespace KnowledgeAssistant.Infrastructure.Search.Chunking;

/// <summary>
/// Derives a chunk's identifier from its document and its position.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why derived rather than random.</b> A random id per run means re-processing
/// a document writes an entirely new set of chunks, leaving the previous set
/// orphaned in whatever index holds them — still matching queries, and
/// identifiable only by ids nobody recorded. A derived id makes reprocessing an
/// update: same document, same position, same key.
/// </para>
/// <para>
/// <b>Why a hash rather than a composite key.</b> The natural key is
/// (document, order), but the consumers of this value want a single
/// <see cref="Guid"/>. Hashing the pair produces one without inventing a
/// registry to map between them.
/// </para>
/// <para>
/// <b>Why version 8 and SHA-256.</b> RFC 9562 defines version 5 for exactly this
/// purpose, but specifies SHA-1 for it — an algorithm that security analyzers
/// flag on sight, and that would need a suppression and an explanation on every
/// future audit. Version 8 is the standard's own escape hatch for
/// implementation-defined derivation, so using it with SHA-256 stays inside the
/// specification while keeping a clean bill of health. The version and variant
/// bits are set exactly as the RFC requires, so the result is a well-formed UUID
/// that any parser will accept.
/// </para>
/// <para>
/// This is not a security boundary. The hash exists to distribute identifiers,
/// not to protect anything, and nothing downstream trusts a chunk id.
/// </para>
/// </remarks>
internal static class ChunkIdFactory
{
    /// <summary>
    /// Creates the stable identifier for a chunk.
    /// </summary>
    /// <param name="documentId">The document the chunk belongs to.</param>
    /// <param name="chunkOrder">The chunk's zero-based position.</param>
    internal static Guid Create(Guid documentId, int chunkOrder)
    {
        // 16 bytes of document id followed by 4 bytes of order. Big-endian for
        // both, so the derivation does not depend on the byte order of whichever
        // machine happens to run it — a chunk id must be reproducible on a
        // developer laptop and a Linux container alike.
        Span<byte> source = stackalloc byte[20];
        _ = documentId.TryWriteBytes(source[..16], bigEndian: true, out _);
        BinaryPrimitives.WriteInt32BigEndian(source[16..], chunkOrder);

        Span<byte> hash = stackalloc byte[32];
        _ = SHA256.HashData(source, hash);

        Span<byte> id = hash[..16];

        // RFC 9562 §4.2: the version occupies the high nibble of octet 6, and the
        // variant the two high bits of octet 8. Without these the value would be a
        // 128-bit number that merely looks like a UUID.
        id[6] = (byte)((id[6] & 0x0F) | 0x80);
        id[8] = (byte)((id[8] & 0x3F) | 0x80);

        return new Guid(id, bigEndian: true);
    }
}
