using Azure.Storage.Blobs;
using KnowledgeAssistant.Infrastructure.Azure.Blob;
using Microsoft.Extensions.Options;

namespace KnowledgeAssistant.Infrastructure.Diagnostics;

/// <summary>
/// Readiness check for the documents container.
/// </summary>
/// <remarks>
/// <para>
/// <b>It asks whether the container exists, not whether the account responds.</b>
/// The account answering proves the network works. What ingestion actually needs
/// is that <i>this</i> container is present and that this instance's managed
/// identity is allowed to see it — and a missing role assignment is by far the
/// most likely way a correctly deployed instance still cannot store a document.
/// A reachability-only probe reports healthy through exactly that failure.
/// </para>
/// <para>
/// It deliberately does not create the container. A readiness probe that mutates
/// the resources it inspects is a probe that can cause an incident, and the
/// creation path belongs to the storage adapter, which owns that decision.
/// </para>
/// </remarks>
internal sealed class BlobStorageHealthCheck : DependencyHealthCheck
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly string _containerName;

    /// <summary>Initialises the check.</summary>
    public BlobStorageHealthCheck(
        BlobServiceClient blobServiceClient,
        IOptions<BlobStorageOptions> storageOptions,
        IOptionsMonitor<HealthProbeOptions> probeOptions,
        TimeProvider timeProvider)
        : base(probeOptions, timeProvider)
    {
        ArgumentNullException.ThrowIfNull(storageOptions);

        _blobServiceClient = blobServiceClient;
        _containerName = storageOptions.Value.DocumentsContainer;
    }

    /// <inheritdoc />
    protected override string DependencyName => "Blob storage";

    /// <inheritdoc />
    protected override async Task ProbeAsync(CancellationToken cancellationToken)
    {
        BlobContainerClient container = _blobServiceClient.GetBlobContainerClient(_containerName);

        bool exists = await container.ExistsAsync(cancellationToken).ConfigureAwait(false);

        if (!exists)
        {
            throw new InvalidOperationException(
                $"The container '{_containerName}' does not exist or is not visible to this identity.");
        }
    }
}
