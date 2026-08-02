using Azure.Search.Documents.Indexes;
using KnowledgeAssistant.Infrastructure.Search;
using Microsoft.Extensions.Options;

namespace KnowledgeAssistant.Infrastructure.Diagnostics;

/// <summary>
/// Readiness check for the search service.
/// </summary>
/// <remarks>
/// <para>
/// <b>It asks the service about itself, not about an index.</b> Both indexes in
/// this solution are created on first use by their adapters, so a freshly
/// deployed instance legitimately has neither — and a check that demanded them
/// would report a healthy service as unready until somebody uploaded a document.
/// Service statistics prove the endpoint resolves, TLS completes, and the managed
/// identity is authorised, which is the whole of what readiness can honestly
/// assert here.
/// </para>
/// <para>
/// It is also the cheapest call the service offers, which matters for something
/// polled on a timer.
/// </para>
/// </remarks>
internal sealed class AzureSearchHealthCheck : DependencyHealthCheck
{
    private readonly SearchIndexClient _indexClient;

    /// <summary>Initialises the check.</summary>
    public AzureSearchHealthCheck(
        SearchIndexClient indexClient,
        IOptionsMonitor<HealthProbeOptions> probeOptions,
        TimeProvider timeProvider)
        : base(probeOptions, timeProvider)
    {
        _indexClient = indexClient;
    }

    /// <inheritdoc />
    protected override string DependencyName => "Azure AI Search";

    /// <inheritdoc />
    protected override async Task ProbeAsync(CancellationToken cancellationToken) =>
        await _indexClient.GetServiceStatisticsAsync(cancellationToken).ConfigureAwait(false);
}
