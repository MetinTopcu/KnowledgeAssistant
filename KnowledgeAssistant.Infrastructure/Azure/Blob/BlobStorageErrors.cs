using KnowledgeAssistant.Domain.Common;

namespace KnowledgeAssistant.Infrastructure.Azure.Blob;

/// <summary>
/// The failures the blob storage adapter can report.
/// </summary>
/// <remarks>
/// <para>
/// Every description here is deliberately generic. Azure's own exception
/// messages carry account names, container names, request ids, and occasionally
/// the reason a credential chain failed — useful in a log, and an unnecessary
/// disclosure in an HTTP response to an unauthenticated caller. The adapter logs
/// the exception in full and returns one of these instead; the client correlates
/// via the <c>traceId</c> already present on every problem response.
/// </para>
/// <para>
/// All are <see cref="ErrorType.Failure"/>, so they surface as 500. None is a
/// <see cref="ErrorType.Validation"/> failure: the caller's request was
/// well-formed, and nothing they change about it would help.
/// </para>
/// </remarks>
internal static class BlobStorageErrors
{
    /// <summary>The container could not be created or confirmed to exist.</summary>
    internal static readonly Error ContainerUnavailable = Error.Failure(
        "Storage.ContainerUnavailable",
        "Document storage is currently unavailable.");

    /// <summary>The upload request was rejected by the storage service.</summary>
    internal static readonly Error UploadFailed = Error.Failure(
        "Storage.UploadFailed",
        "The document could not be stored.");

    /// <summary>A blob already exists at the generated name.</summary>
    /// <remarks>
    /// Should be unreachable: the name derives from a version 7 GUID. If it ever
    /// fires, the identifier generation is broken rather than the storage
    /// account, and overwriting silently would destroy a document.
    /// </remarks>
    internal static readonly Error BlobAlreadyExists = Error.Failure(
        "Storage.BlobAlreadyExists",
        "The document could not be stored because the generated name was already in use.");

    /// <summary>The application could not authenticate to Azure Storage.</summary>
    internal static readonly Error AuthenticationFailed = Error.Failure(
        "Storage.AuthenticationFailed",
        "Document storage is currently unavailable.");

    /// <summary>The requested blob does not exist.</summary>
    /// <remarks>
    /// Distinct from <see cref="DownloadFailed"/> because it means something
    /// quite different: not that storage is unwell, but that the name being asked
    /// for is wrong or the content has been removed. During ingestion it should be
    /// unreachable — the blob was written moments earlier — and if it ever fires
    /// there, the cause is a lifecycle policy or an external deletion rather than
    /// a transient fault, and no amount of retrying will help.
    /// </remarks>
    internal static readonly Error BlobNotFound = Error.Failure(
        "Storage.BlobNotFound",
        "The stored document could not be found.");

    /// <summary>The blob could not be read back.</summary>
    internal static readonly Error DownloadFailed = Error.Failure(
        "Storage.DownloadFailed",
        "The stored document could not be read.");
}
