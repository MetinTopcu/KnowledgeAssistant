namespace KnowledgeAssistant.Domain.Common;

/// <summary>
/// Classifies a failure so that outer layers can translate it into a transport
/// concern (an HTTP status code) without inspecting error codes by string.
/// </summary>
/// <remarks>
/// Only the members this codebase actually produces are declared. Adding
/// <c>NotFound</c> or <c>Conflict</c> speculatively would create branches in
/// the HTTP mapper that no test could reach and no caller could trigger; they
/// get added by the slice that first needs them.
/// </remarks>
public enum ErrorType
{
    /// <summary>An unexpected failure. Maps to 500.</summary>
    Failure = 0,

    /// <summary>The request was structurally or semantically invalid. Maps to 400.</summary>
    Validation = 1,
}
