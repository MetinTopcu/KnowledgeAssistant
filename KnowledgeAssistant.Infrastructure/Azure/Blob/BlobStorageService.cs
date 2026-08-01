using System.Globalization;
using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using KnowledgeAssistant.Application.Interfaces;
using KnowledgeAssistant.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAssistant.Infrastructure.Azure.Blob;

/// <summary>
/// Stores documents in Azure Blob Storage.
/// </summary>
/// <remarks>
/// <para>
/// <b>Registered as a singleton.</b> <see cref="BlobServiceClient"/> is
/// thread-safe and holds a pooled <c>HttpClient</c> plus a cached access token;
/// constructing one per request would re-run the credential chain and exhaust
/// sockets under load. Singleton lifetime is also what makes the
/// container-existence cache below worth having.
/// </para>
/// <para>
/// <b>Failures become values.</b> Every Azure exception is logged in full and
/// converted to a <see cref="BlobStorageErrors"/> value. The logging is not
/// optional decoration: converting an exception to a value without recording it
/// first would discard the stack trace and the Azure request id, leaving an
/// outage with nothing to diagnose from.
/// </para>
/// </remarks>
internal sealed partial class BlobStorageService : IBlobStorageService, IDisposable
{
    private readonly BlobContainerClient _containerClient;
    private readonly ILogger<BlobStorageService> _logger;

    // Guards the one-time container check. A plain Lazy<Task> is the tempting
    // alternative and the wrong one: it caches a faulted task permanently, so a
    // single transient failure at startup would poison every later upload for
    // the lifetime of the process. This pair re-attempts until it succeeds once.
    private readonly SemaphoreSlim _containerInitializationLock = new(1, 1);
    private volatile bool _containerVerified;

    /// <summary>Initialises the adapter.</summary>
    public BlobStorageService(
        BlobServiceClient blobServiceClient,
        IOptions<BlobStorageOptions> options,
        ILogger<BlobStorageService> logger)
    {
        ArgumentNullException.ThrowIfNull(blobServiceClient);
        ArgumentNullException.ThrowIfNull(options);

        _containerClient = blobServiceClient.GetBlobContainerClient(options.Value.DocumentsContainer);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<BlobUploadResult>> UploadAsync(
        Guid documentId,
        string fileName,
        string contentType,
        Stream content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        Result containerResult = await EnsureContainerExistsAsync(cancellationToken).ConfigureAwait(false);

        if (containerResult.IsFailure)
        {
            return Result.Failure<BlobUploadResult>(containerResult.Error);
        }

        string blobName = BuildBlobName(documentId, fileName);
        BlobClient blobClient = _containerClient.GetBlobClient(blobName);

        var uploadOptions = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },

            // Recorded for operators tracing a blob back to a request. The
            // original name is percent-encoded because blob metadata travels as
            // an HTTP header and must be ASCII — an unencoded "rapor-şubat.pdf"
            // is rejected by the service, which would turn a perfectly valid
            // upload into a 500 for non-English filenames.
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["documentId"] = documentId.ToString(),
                ["originalFileName"] = Uri.EscapeDataString(fileName),
            },

            // Fail rather than overwrite if the name is somehow taken. Silent
            // overwrite is data loss that leaves no trace.
            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
        };

        try
        {
            await blobClient.UploadAsync(content, uploadOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException exception) when (exception.Status == 409)
        {
            LogBlobAlreadyExists(exception, blobName);
            return Result.Failure<BlobUploadResult>(BlobStorageErrors.BlobAlreadyExists);
        }
        catch (RequestFailedException exception)
        {
            LogUploadFailed(exception, blobName, exception.Status, exception.ErrorCode);
            return Result.Failure<BlobUploadResult>(BlobStorageErrors.UploadFailed);
        }
        catch (AuthenticationFailedException exception)
        {
            LogAuthenticationFailed(exception);
            return Result.Failure<BlobUploadResult>(BlobStorageErrors.AuthenticationFailed);
        }
        catch (AggregateException exception)
        {
            // Azure's retry policy throws this — not RequestFailedException — once
            // every attempt has failed at the transport level (DNS, TLS, socket).
            // Catching only RequestFailedException lets a total storage outage
            // escape as an unhandled exception, which is exactly what happened the
            // first time this path was exercised against an unreachable account.
            LogTransportFailed(exception, blobName);
            return Result.Failure<BlobUploadResult>(BlobStorageErrors.UploadFailed);
        }

        // Measured rather than taken from the client's declared length. A seekable
        // stream still reports Length; a forward-only one has advanced to exactly
        // the number of bytes read.
        long sizeInBytes = content.CanSeek ? content.Length : content.Position;

        LogUploadSucceeded(blobName, sizeInBytes);

        return new BlobUploadResult(blobName, blobClient.Uri, sizeInBytes);
    }

    /// <inheritdoc />
    public async Task<Result<Stream>> DownloadAsync(string blobName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobName);

        // No container check here, unlike the upload path. A missing container
        // produces the same 404 as a missing blob, and creating one on a read
        // would be creating somewhere for content that by definition is not there.
        BlobClient blobClient = _containerClient.GetBlobClient(blobName);

        try
        {
            // DownloadContent rather than DownloadStreaming: it buffers the blob
            // and returns a seekable stream. The alternative is forward-only, and
            // every consumer that needs to seek — the PDF parser does — would copy
            // it into memory anyway, so streaming would buy an extra copy rather
            // than save one. Bounded by the endpoint's request size limit.
            Response<BlobDownloadResult> response = await blobClient
                .DownloadContentAsync(cancellationToken)
                .ConfigureAwait(false);

            Stream content = response.Value.Content.ToStream();

            LogDownloadSucceeded(blobName, content.Length);

            return content;
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            LogBlobNotFound(exception, blobName);
            return Result.Failure<Stream>(BlobStorageErrors.BlobNotFound);
        }
        catch (RequestFailedException exception)
        {
            LogDownloadFailed(exception, blobName, exception.Status, exception.ErrorCode);
            return Result.Failure<Stream>(BlobStorageErrors.DownloadFailed);
        }
        catch (AuthenticationFailedException exception)
        {
            LogAuthenticationFailed(exception);
            return Result.Failure<Stream>(BlobStorageErrors.AuthenticationFailed);
        }
        catch (AggregateException exception)
        {
            // Retries exhausted at the transport level arrive here, not as
            // RequestFailedException — the same trap the upload path guards.
            LogTransportFailed(exception, blobName);
            return Result.Failure<Stream>(BlobStorageErrors.DownloadFailed);
        }
    }

    /// <summary>
    /// Creates the container on first use, then remembers that it exists.
    /// </summary>
    /// <remarks>
    /// Calling <c>CreateIfNotExists</c> on every upload costs a round trip per
    /// request for a condition that changes at most once in the lifetime of the
    /// deployment. The double-checked flag keeps the steady-state cost at a
    /// single volatile read.
    /// <para>
    /// The container is created private. Public access would make every stored
    /// document world-readable to anyone who guesses a URL, which for an
    /// enterprise knowledge base is the whole corpus.
    /// </para>
    /// </remarks>
    private async Task<Result> EnsureContainerExistsAsync(CancellationToken cancellationToken)
    {
        if (_containerVerified)
        {
            return Result.Success();
        }

        await _containerInitializationLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_containerVerified)
            {
                return Result.Success();
            }

            await _containerClient
                .CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            _containerVerified = true;
            return Result.Success();
        }
        catch (RequestFailedException exception)
        {
            LogContainerUnavailable(exception, _containerClient.Name, exception.Status, exception.ErrorCode);
            return Result.Failure(BlobStorageErrors.ContainerUnavailable);
        }
        catch (AuthenticationFailedException exception)
        {
            LogAuthenticationFailed(exception);
            return Result.Failure(BlobStorageErrors.AuthenticationFailed);
        }
        catch (AggregateException exception)
        {
            // See the equivalent catch in UploadAsync: retries exhausted at the
            // transport level arrive here, not as RequestFailedException.
            LogTransportFailed(exception, _containerClient.Name);
            return Result.Failure(BlobStorageErrors.ContainerUnavailable);
        }
        finally
        {
            _containerInitializationLock.Release();
        }
    }

    /// <summary>
    /// Builds a collision-free, date-partitioned blob name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client's file name is never used as the blob name. It is
    /// attacker-controlled, frequently duplicated across users, and may contain
    /// characters that are legal in a file system and awkward in a URL. Two users
    /// uploading <c>report.pdf</c> must not collide.
    /// </para>
    /// <para>
    /// The date prefix exists so lifecycle-management rules can act on a prefix,
    /// and so a listing operation can be scoped to a day instead of enumerating
    /// the entire container. The version 7 GUID supplies uniqueness and sorts in
    /// creation order.
    /// </para>
    /// </remarks>
    private static string BuildBlobName(Guid documentId, string fileName)
    {
        // Derived from the identifier's own timestamp rather than the wall clock,
        // so the prefix always matches the id and a retry cannot land the same
        // document under two different dates.
        DateTimeOffset createdAt = documentId.Version == 7
            ? DateTimeOffset.FromUnixTimeMilliseconds(ExtractUnixMilliseconds(documentId))
            : DateTimeOffset.UtcNow;

        string extension = Path.GetExtension(fileName).ToLowerInvariant();

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{createdAt:yyyy}/{createdAt:MM}/{createdAt:dd}/{documentId}{extension}");
    }

    /// <summary>
    /// Reads the 48-bit big-endian Unix millisecond timestamp from the high bits
    /// of a version 7 GUID, as specified by RFC 9562.
    /// </summary>
    private static long ExtractUnixMilliseconds(Guid documentId)
    {
        Span<byte> bytes = stackalloc byte[16];
        _ = documentId.TryWriteBytes(bytes, bigEndian: true, out _);

        long milliseconds = 0;
        for (int index = 0; index < 6; index++)
        {
            milliseconds = (milliseconds << 8) | bytes[index];
        }

        return milliseconds;
    }

    /// <summary>Releases the initialisation lock.</summary>
    public void Dispose() => _containerInitializationLock.Dispose();

    // Source-generated logging. The generator emits a cached delegate per
    // message, so a disabled log level costs a branch instead of boxing every
    // argument — which matters on a path that runs per upload.

    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "Stored blob {BlobName} ({SizeInBytes} bytes).")]
    private partial void LogUploadSucceeded(string blobName, long sizeInBytes);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Error,
        Message = "Failed to upload blob {BlobName}. Storage returned {Status} ({ErrorCode}).")]
    private partial void LogUploadFailed(Exception exception, string blobName, int status, string? errorCode);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Error,
        Message = "Blob {BlobName} already exists; refusing to overwrite.")]
    private partial void LogBlobAlreadyExists(Exception exception, string blobName);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Error,
        Message = "Container {ContainerName} is unavailable. Storage returned {Status} ({ErrorCode}).")]
    private partial void LogContainerUnavailable(Exception exception, string containerName, int status, string? errorCode);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Error,
        Message = "Failed to authenticate to Azure Storage. Verify the managed identity and its RBAC role assignments.")]
    private partial void LogAuthenticationFailed(Exception exception);

    [LoggerMessage(
        EventId = 1005,
        Level = LogLevel.Error,
        Message = "Azure Storage was unreachable for {Target} after all retries were exhausted.")]
    private partial void LogTransportFailed(Exception exception, string target);

    [LoggerMessage(
        EventId = 1006,
        Level = LogLevel.Debug,
        Message = "Read blob {BlobName} ({SizeInBytes} bytes).")]
    private partial void LogDownloadSucceeded(string blobName, long sizeInBytes);

    [LoggerMessage(
        EventId = 1007,
        Level = LogLevel.Error,
        Message = "Blob {BlobName} was not found.")]
    private partial void LogBlobNotFound(Exception exception, string blobName);

    [LoggerMessage(
        EventId = 1008,
        Level = LogLevel.Error,
        Message = "Failed to read blob {BlobName}. Storage returned {Status} ({ErrorCode}).")]
    private partial void LogDownloadFailed(Exception exception, string blobName, int status, string? errorCode);
}
