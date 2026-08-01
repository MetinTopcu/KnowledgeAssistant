namespace KnowledgeAssistant.Api.Controllers;

/// <summary>
/// The JSON body of a question request.
/// </summary>
/// <remarks>
/// <para>
/// A transport type, deliberately separate from <c>AskQuestionQuery</c>. It
/// exists so the wire contract can carry an optional <c>topK</c> with a default,
/// while the query itself takes a definite value — the controller resolves the
/// default, and nothing downstream has to interpret a null.
/// </para>
/// <para>
/// It carries <b>no validation attributes at all</b>, and that is deliberate.
/// A <c>[Required]</c> here would be enforced by model binding <i>before</i> the
/// query reaches its validator, so a blank question would come back as the
/// framework's "The Question field is required." instead of this system's
/// <c>Question.Missing</c> — two error shapes for one rule, and the one clients
/// would actually receive is the one that changes with the framework. Every
/// acceptance rule lives in the query's validator, where a non-HTTP caller
/// reaches it too.
/// </para>
/// </remarks>
public sealed class AskQuestionRequest
{
    /// <summary>The question to answer.</summary>
    public string Question { get; init; } = string.Empty;

    /// <summary>
    /// How many chunks to retrieve as grounding, or null for the default.
    /// </summary>
    public int? TopK { get; init; }
}
