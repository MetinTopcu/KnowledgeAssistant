using System.Diagnostics.Metrics;
using KnowledgeAssistant.Application.Diagnostics;
using KnowledgeAssistant.Application.Interfaces;
using KnowledgeAssistant.Domain.Common;

namespace KnowledgeAssistant.Application.Queries.Documents.Ask;

/// <summary>
/// Retrieval and answering measurements.
/// </summary>
/// <remarks>
/// <para>
/// <b>Token usage is here because it is the bill.</b> Nothing else in this system
/// costs money per request, and a prompt-token histogram is what turns "the model
/// spend doubled" into "the context budget started admitting twice as many
/// chunks" without an investigation.
/// </para>
/// <para>
/// <b>The unanswered counter is the quality signal.</b> A question that retrieves
/// nothing is a success by every technical measure — 200, fast, no error — and is
/// also the system failing to do its job. It is invisible to latency and error
/// dashboards, so it gets an instrument of its own; a rising rate means the corpus
/// has gaps or that retrieval has regressed.
/// </para>
/// </remarks>
internal sealed class AskQuestionMetrics : IResponseMetricsRecorder<Result<AskQuestionResponse>>
{
    private readonly Counter<long> _questionsAnswered;
    private readonly Counter<long> _questionsUnanswered;
    private readonly Histogram<int> _retrievedChunks;
    private readonly Histogram<int> _citationsReturned;
    private readonly Histogram<int> _promptTokens;
    private readonly Histogram<int> _completionTokens;

    /// <summary>Creates the instruments on the host's meter.</summary>
    public AskQuestionMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        Meter meter = meterFactory.Create(ApplicationDiagnostics.MeterName);

        _questionsAnswered = meter.CreateCounter<long>(
            "knowledgeassistant.questions.answered",
            unit: "{question}",
            description: "Questions answered from retrieved evidence.");

        _questionsUnanswered = meter.CreateCounter<long>(
            "knowledgeassistant.questions.unanswered",
            unit: "{question}",
            description: "Questions that retrieved no evidence and were declined.");

        _retrievedChunks = meter.CreateHistogram<int>(
            "knowledgeassistant.retrieval.chunks",
            unit: "{chunk}",
            description: "Chunks returned by the retrieval step.");

        _citationsReturned = meter.CreateHistogram<int>(
            "knowledgeassistant.answer.citations",
            unit: "{citation}",
            description: "Citations accompanying an answer, after the context budget was applied.");

        _promptTokens = meter.CreateHistogram<int>(
            "knowledgeassistant.model.prompt_tokens",
            unit: "{token}",
            description: "Prompt tokens consumed per answered question.");

        _completionTokens = meter.CreateHistogram<int>(
            "knowledgeassistant.model.completion_tokens",
            unit: "{token}",
            description: "Completion tokens produced per answered question.");
    }

    /// <inheritdoc />
    public void Record(Result<AskQuestionResponse> response)
    {
        ArgumentNullException.ThrowIfNull(response);

        AskQuestionResponse answer = response.Value;

        _retrievedChunks.Record(answer.RetrievedChunkCount);

        // No token usage means the model was never called, which happens on
        // exactly one path: retrieval came back empty and the canned no-evidence
        // answer was returned. That is the case this counter exists to surface.
        if (answer.TokenUsage is null)
        {
            _questionsUnanswered.Add(1);
            return;
        }

        _questionsAnswered.Add(1);
        _citationsReturned.Record(answer.Citations.Count);

        TokenUsage usage = answer.TokenUsage;

        _promptTokens.Record(usage.PromptTokens);
        _completionTokens.Record(usage.CompletionTokens);
    }
}
