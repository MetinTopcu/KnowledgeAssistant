using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;

namespace KnowledgeAssistant.Infrastructure.Search;

/// <summary>
/// Defines the index schema from the model that is written to it.
/// </summary>
/// <remarks>
/// <para>
/// <c>Search/README.md</c> states the rule this class implements: the index
/// schema is a <b>derived artefact</b> — code-defined and rebuildable, never
/// hand-edited in the portal. A schema maintained by hand in two places drifts
/// between environments, and eventually nobody can say what production's schema
/// actually is.
/// </para>
/// <para>
/// Deriving the fields from <see cref="SearchDocument"/> with
/// <see cref="FieldBuilder"/> goes one step further: the schema cannot drift from
/// the model even in principle, because there is only one definition. Adding a
/// property to the model is the only way to add a field.
/// </para>
/// </remarks>
internal static class DocumentIndexSchema
{
    /// <summary>Builds the index definition for <paramref name="indexName"/>.</summary>
    internal static SearchIndex Build(string indexName) =>
        new(indexName, new FieldBuilder().Build(typeof(SearchDocument)));
}
