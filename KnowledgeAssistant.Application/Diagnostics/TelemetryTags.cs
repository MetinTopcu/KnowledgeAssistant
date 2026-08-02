namespace KnowledgeAssistant.Application.Diagnostics;

/// <summary>
/// The attribute names carried by this layer's spans and measurements.
/// </summary>
/// <remarks>
/// <para>
/// Centralised because a tag name is a query contract, not a string literal. A
/// dashboard, an alert rule, and a saved Kusto query all bind to these; renaming
/// one in a single call site breaks the alert silently, which is the failure mode
/// that makes observability untrustworthy exactly when it is needed.
/// </para>
/// <para>
/// The names follow OpenTelemetry semantic-convention style — lowercase, dotted,
/// namespaced by their subject — so they sit alongside the <c>http.*</c> and
/// <c>db.*</c> attributes the instrumentation libraries emit rather than
/// competing with them.
/// </para>
/// </remarks>
public static class TelemetryTags
{
    /// <summary>The CQRS request type name, for example <c>UploadDocumentCommand</c>.</summary>
    public const string RequestName = "knowledgeassistant.request.name";

    /// <summary>Either <c>command</c> or <c>query</c>.</summary>
    public const string RequestKind = "knowledgeassistant.request.kind";

    /// <summary>Either <c>success</c> or <c>failure</c>.</summary>
    public const string Outcome = "knowledgeassistant.outcome";

    /// <summary>
    /// The domain error code of a failed result, for example <c>Chat.RateLimited</c>.
    /// </summary>
    /// <remarks>
    /// The code, never the description. Codes are a closed, low-cardinality set
    /// that groups cleanly in a chart; descriptions are free text that can embed
    /// a file name or a question, which would both explode cardinality and put
    /// user-supplied content into telemetry.
    /// </remarks>
    public const string ErrorCode = "knowledgeassistant.error.code";

    /// <summary>The error classification, for example <c>Validation</c>.</summary>
    public const string ErrorType = "knowledgeassistant.error.type";

    /// <summary>The caller-supplied correlation identifier, when one was sent.</summary>
    public const string CorrelationId = "knowledgeassistant.correlation.id";
}
