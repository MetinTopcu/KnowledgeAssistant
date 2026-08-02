namespace KnowledgeAssistant.Application.Diagnostics;

/// <summary>
/// A slice's own measurements, recorded from its successful response.
/// </summary>
/// <typeparam name="TResponse">The slice's response type.</typeparam>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> The pipeline behaviour can time any request and report
/// whether it succeeded, because every slice returns a <c>Result</c>. What it
/// cannot know is that an ingestion produced eleven chunks or that an answer cost
/// four hundred tokens — and those are the numbers an operator actually watches.
/// Teaching the behaviour about them would drag every slice's vocabulary into a
/// generic type, which is precisely the coupling vertical slices exist to avoid.
/// </para>
/// <para>
/// So a slice may supply one of these and keep its metrics beside its handler,
/// its validator, and its response. The behaviour resolves it if it is
/// registered and does nothing if it is not, so adding telemetry to a slice never
/// requires editing shared code, and a slice with nothing worth measuring carries
/// no ceremony.
/// </para>
/// <para>
/// <typeparamref name="TResponse"/> is the slice's <i>mediator</i> response —
/// <c>Result&lt;UploadDocumentResponse&gt;</c>, not <c>UploadDocumentResponse</c>
/// — because that is the type the pipeline is generic over. The behaviour calls
/// this only after confirming the result succeeded, so an implementation may read
/// <c>Value</c> without checking again. A failure has nothing to measure, and its
/// error code is already recorded by the behaviour.
/// </para>
/// </remarks>
public interface IResponseMetricsRecorder<in TResponse>
{
    /// <summary>Records the slice's measurements for one successful response.</summary>
    void Record(TResponse response);
}
