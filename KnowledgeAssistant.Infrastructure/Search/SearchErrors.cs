using KnowledgeAssistant.Domain.Common;

namespace KnowledgeAssistant.Infrastructure.Search;

/// <summary>
/// The failures the search indexing adapter can report.
/// </summary>
/// <remarks>
/// <para>
/// Generic descriptions, for the same reason as <c>BlobStorageErrors</c>: Azure's
/// own messages carry service names, index names, request ids, and occasionally
/// the reason a credential chain failed. That is useful in a log and an
/// unnecessary disclosure in an HTTP response. The adapter logs the exception in
/// full and returns one of these; the client correlates through the
/// <c>traceId</c> already stamped on every problem response.
/// </para>
/// <para>
/// All are <see cref="ErrorType.Failure"/>, so they surface as 500. None is a
/// <see cref="ErrorType.Validation"/> failure: the caller's request was
/// well-formed and nothing they could change about it would help.
/// </para>
/// </remarks>
internal static class SearchErrors
{
    /// <summary>The index could not be created or confirmed to exist.</summary>
    internal static readonly Error IndexUnavailable = Error.Failure(
        "Search.IndexUnavailable",
        "Document indexing is currently unavailable.");

    /// <summary>The service rejected the indexing request outright.</summary>
    internal static readonly Error IndexingFailed = Error.Failure(
        "Search.IndexingFailed",
        "The document could not be indexed.");

    /// <summary>
    /// The request was accepted but the document itself was not indexed.
    /// </summary>
    /// <remarks>
    /// A distinct error because it is a distinct condition, and the one most
    /// easily missed: Azure AI Search answers <c>200</c> for a batch in which
    /// individual documents failed. Collapsing it into
    /// <see cref="IndexingFailed"/> would hide the difference between "the
    /// service refused us" and "the service accepted us and dropped the
    /// document" — which have different causes and different fixes.
    /// </remarks>
    internal static readonly Error DocumentRejected = Error.Failure(
        "Search.DocumentRejected",
        "The document could not be indexed.");

    /// <summary>The application could not authenticate to Azure AI Search.</summary>
    internal static readonly Error AuthenticationFailed = Error.Failure(
        "Search.AuthenticationFailed",
        "Document indexing is currently unavailable.");
}
