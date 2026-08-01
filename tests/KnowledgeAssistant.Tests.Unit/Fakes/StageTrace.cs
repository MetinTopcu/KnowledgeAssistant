namespace KnowledgeAssistant.Tests.Unit.Fakes;

/// <summary>
/// Records which ports a handler called, in order, and with which token.
/// </summary>
/// <remarks>
/// Shared by every orchestration test. Ordering is the property that matters
/// most in a pipeline — "the document index is written last" and "the model is
/// not called when retrieval is empty" are both statements about sequence — and
/// asserting it needs one recorder that all the fakes write to.
/// </remarks>
internal sealed class StageTrace
{
    public List<string> Stages { get; } = [];

    public List<CancellationToken> Tokens { get; } = [];

    public void Record(string stage, CancellationToken cancellationToken)
    {
        Stages.Add(stage);
        Tokens.Add(cancellationToken);
    }

    public string Sequence => string.Join(" -> ", Stages);
}
