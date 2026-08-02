namespace KnowledgeAssistant.Application.Agents;

/// <summary>
/// The standing instructions the agent answers under.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is in Application and not beside the adapter that sends it.</b>
/// It is the same argument <c>GroundedPromptBuilder</c> makes: these sentences
/// encode what this product considers an acceptable answer — search before
/// answering, ground every claim, cite, decline when the corpus is silent. Those
/// rules must survive replacing the agent runtime behind them, so they belong
/// with the use case rather than inside a vendor adapter. Moving to a different
/// agent platform should change how these are transmitted, not what they say.
/// </para>
/// <para>
/// <b>What it deliberately does not contain.</b> No tool name, no function
/// schema, no argument shape, no mention of JSON. Those are transport, they are
/// the adapter's business, and naming them here would put a vendor's calling
/// convention into the layer whose entire purpose is not to have one. The
/// instructions describe a <i>capability</i> — "you can search the documents" —
/// and Infrastructure decides what that capability is called on the wire.
/// </para>
/// <para>
/// <b>The first rule carries the most weight.</b> An agent chooses whether to
/// search, and the cheapest thing it can do is not bother — answering from
/// training data, fluently and plausibly, about a corpus it never opened. That
/// failure is invisible in the output and is what the opening instruction and the
/// reported search count exist to prevent between them.
/// </para>
/// </remarks>
public static class AgentInstructions
{
    /// <summary>The instruction text the agent is provisioned with.</summary>
    /// <remarks>
    /// Public because the adapter that provisions the agent must read it, and
    /// that adapter is in another assembly. It is the text itself that crosses the
    /// boundary; nothing about how it is delivered comes back.
    /// </remarks>
    public const string SystemPrompt =
        """
        You are a knowledge assistant answering questions about a private collection of documents.

        You can search that collection. You cannot see it any other way, and you know nothing about its
        contents until you search.

        Rules:
        - Always search before answering. Never answer a question about the documents from prior knowledge.
        - If the first search returns nothing useful, search again with different wording before giving up.
        - Base every statement on the passages the search returned. Do not infer beyond what they say.
        - Cite the passages you used inline, as [1], [2], and so on, using the reference number each passage
          was given. Cite every claim.
        - If the passages do not contain enough information to answer, say so plainly and state what is
          missing. Do not guess, and do not fill the gap from prior knowledge.
        - If the passages disagree, say that they disagree and cite each side.
        - Answer in the language of the question.
        - Be concise. Do not repeat the question, and do not narrate your searching.
        """;

    /// <summary>The description recorded against the provisioned agent.</summary>
    /// <remarks>
    /// Shown in the Azure AI Foundry portal beside the agent. Worth setting
    /// because the alternative is an unlabelled agent that nobody dares delete.
    /// </remarks>
    public const string Description =
        "Answers questions about the ingested document corpus, grounded in passages it retrieves itself.";
}
