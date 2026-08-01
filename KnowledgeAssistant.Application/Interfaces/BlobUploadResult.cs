namespace KnowledgeAssistant.Application.Interfaces;

/// <summary>
/// The outcome of storing a file in blob storage.
/// </summary>
/// <param name="BlobName">
/// The storage-relative name the content was written under, including any
/// virtual directory prefix.
/// </param>
/// <param name="BlobUri">The absolute location of the stored blob.</param>
/// <param name="SizeInBytes">The number of bytes actually written.</param>
/// <remarks>
/// <para>
/// Deliberately made of primitives and <see cref="Uri"/>. Returning the SDK's
/// <c>BlobContentInfo</c> would put <c>Azure.Storage.Blobs</c> types into the
/// Application layer's public surface, which is exactly the coupling the port
/// exists to prevent — the interface would name a vendor in its signature and
/// no second implementation could satisfy it.
/// </para>
/// <para>
/// <paramref name="SizeInBytes"/> is what the adapter measured, not what the
/// client declared in its <c>Content-Length</c>. The two can differ, and the
/// authoritative number is the one that reached storage.
/// </para>
/// </remarks>
public sealed record BlobUploadResult(
    string BlobName,
    Uri BlobUri,
    long SizeInBytes);
