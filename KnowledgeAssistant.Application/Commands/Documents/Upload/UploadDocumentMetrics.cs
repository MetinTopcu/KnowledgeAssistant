using System.Diagnostics.Metrics;
using KnowledgeAssistant.Application.Diagnostics;
using KnowledgeAssistant.Domain.Common;

namespace KnowledgeAssistant.Application.Commands.Documents.Upload;

/// <summary>
/// Ingestion's own measurements.
/// </summary>
/// <remarks>
/// <para>
/// Lives in the slice, next to the handler and the validator it describes,
/// because these numbers mean nothing outside it and nobody reading the slice
/// should have to look elsewhere to find out what it reports.
/// </para>
/// <para>
/// <b>What these answer that request duration cannot.</b> Ingestion latency is
/// dominated by document size, so a rise in the p99 is usually just a bigger
/// file. Chunks per document is the signal that separates the two: if it collapses
/// while uploads keep succeeding, extraction has quietly started returning almost
/// nothing — the corpus is filling with documents that can never be cited, and no
/// error is raised anywhere.
/// </para>
/// </remarks>
internal sealed class UploadDocumentMetrics : IResponseMetricsRecorder<Result<UploadDocumentResponse>>
{
    private readonly Counter<long> _documentsIngested;
    private readonly Histogram<int> _chunksPerDocument;
    private readonly Histogram<long> _documentSize;

    /// <summary>Creates the instruments on the host's meter.</summary>
    public UploadDocumentMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        Meter meter = meterFactory.Create(ApplicationDiagnostics.MeterName);

        _documentsIngested = meter.CreateCounter<long>(
            "knowledgeassistant.ingestion.documents",
            unit: "{document}",
            description: "Documents ingested successfully.");

        _chunksPerDocument = meter.CreateHistogram<int>(
            "knowledgeassistant.ingestion.chunks_per_document",
            unit: "{chunk}",
            description: "Chunks produced per ingested document.");

        _documentSize = meter.CreateHistogram<long>(
            "knowledgeassistant.ingestion.document_size",
            unit: "By",
            description: "Stored size of an ingested document.");
    }

    /// <inheritdoc />
    public void Record(Result<UploadDocumentResponse> response)
    {
        ArgumentNullException.ThrowIfNull(response);

        UploadDocumentResponse ingested = response.Value;

        _documentsIngested.Add(1);
        _chunksPerDocument.Record(ingested.ChunkCount);

        // The size storage measured, which is also what the response reports —
        // deliberately not the client's declared length, which is unverified.
        _documentSize.Record(ingested.SizeInBytes);
    }
}
