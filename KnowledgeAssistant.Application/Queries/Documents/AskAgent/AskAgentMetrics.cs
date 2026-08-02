using System.Diagnostics.Metrics;
using KnowledgeAssistant.Application.Diagnostics;
using KnowledgeAssistant.Application.Interfaces;
using KnowledgeAssistant.Domain.Common;

namespace KnowledgeAssistant.Application.Queries.Documents.AskAgent;

/// <summary>
/// What the agent did, and what it cost.
/// </summary>
/// <remarks>
/// <para>
/// <b>Searches per answer is the instrument this slice exists to add.</b> The
/// pipeline behaviour already times the request and records whether it succeeded,
/// and those numbers look the same whether the agent consulted the corpus five
/// times or not at all. The distribution of search counts is what turns "the agent
/// endpoint got expensive" into "it started searching four times per question
/// instead of one" without an investigation — and its zero bucket is the
/// ungrounded-answer rate, which no latency or error chart can show.
/// </para>
/// <para>
/// <b>Ungrounded answers get a counter of their own rather than being read off the
/// histogram.</b> A zero-search answer is a success by every technical measure —
/// 200, fast, no error — and is also the system failing to do its job. It is the
/// direct counterpart of the retrieval slice's unanswered counter, and it is worth
/// alerting on, which is awkward against a bucket and trivial against a counter.
/// </para>
/// <para>
/// <b>Tokens are recorded on the same instrument names the retrieval slice
/// uses</b>, distinguished by the request name the behaviour already tags. Two
/// differently named token histograms would make "what does this service spend"
/// a question requiring a union query, and would silently omit whichever slice was
/// added last from every existing cost dashboard.
/// </para>
/// </remarks>
internal sealed class AskAgentMetrics : IResponseMetricsRecorder<Result<AskAgentResponse>>
{
    private readonly Counter<long> _agentQuestionsAnswered;
    private readonly Counter<long> _agentAnswersWithoutSearch;
    private readonly Histogram<int> _searchesPerAnswer;
    private readonly Histogram<int> _sourcesConsulted;
    private readonly Histogram<int> _promptTokens;
    private readonly Histogram<int> _completionTokens;

    /// <summary>Creates the instruments on the host's meter.</summary>
    public AskAgentMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        Meter meter = meterFactory.Create(ApplicationDiagnostics.MeterName);

        _agentQuestionsAnswered = meter.CreateCounter<long>(
            "knowledgeassistant.agent.questions.answered",
            unit: "{question}",
            description: "Questions answered by the agent.");

        _agentAnswersWithoutSearch = meter.CreateCounter<long>(
            "knowledgeassistant.agent.answers.ungrounded",
            unit: "{answer}",
            description: "Agent answers produced without searching the corpus at all.");

        _searchesPerAnswer = meter.CreateHistogram<int>(
            "knowledgeassistant.agent.searches",
            unit: "{search}",
            description: "Corpus searches the agent ran per answer.");

        _sourcesConsulted = meter.CreateHistogram<int>(
            "knowledgeassistant.agent.sources",
            unit: "{source}",
            description: "Distinct passages the agent consulted per answer.");

        // Deliberately the same instrument names the retrieval slice creates. A
        // Meter returns the existing instrument for a repeated name, so these are
        // one time series that both slices write to, separated by the request-name
        // tag the pipeline behaviour attaches.
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
    public void Record(Result<AskAgentResponse> response)
    {
        ArgumentNullException.ThrowIfNull(response);

        AskAgentResponse answer = response.Value;

        _agentQuestionsAnswered.Add(1);
        _searchesPerAnswer.Record(answer.SearchCount);
        _sourcesConsulted.Record(answer.Citations.Count);

        if (answer.SearchCount == 0)
        {
            _agentAnswersWithoutSearch.Add(1);
        }

        if (answer.TokenUsage is not TokenUsage usage)
        {
            return;
        }

        _promptTokens.Record(usage.PromptTokens);
        _completionTokens.Record(usage.CompletionTokens);
    }
}
