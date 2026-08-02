namespace KnowledgeAssistant.Api.Controllers;

/// <summary>
/// The JSON body of a question put to the agent.
/// </summary>
/// <remarks>
/// <para>
/// A transport type, separate from <c>AskAgentQuery</c> for the same reason
/// <see cref="AskQuestionRequest"/> is separate from its query: the wire contract
/// carries an optional bound with a default, while the query takes a definite
/// value, so the controller resolves the default and nothing downstream has to
/// interpret a null.
/// </para>
/// <para>
/// It carries <b>no validation attributes</b>, deliberately. A <c>[Required]</c>
/// here would be enforced by model binding before the query reached its validator,
/// so a blank question would come back as the framework's message instead of this
/// system's <c>Question.Missing</c> — two error shapes for one rule. Every
/// acceptance rule lives in the query's validator, where a non-HTTP caller reaches
/// it too.
/// </para>
/// </remarks>
public sealed class AskAgentRequest
{
    /// <summary>The question to answer.</summary>
    public string Question { get; init; } = string.Empty;

    /// <summary>
    /// How many passages any one of the agent's searches may return, or null for
    /// the default.
    /// </summary>
    public int? MaxSources { get; init; }
}
