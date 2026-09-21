using System.ComponentModel.DataAnnotations;

namespace KnowledgeAssistant.Infrastructure.Search;

/// <summary>
/// Configuration for the Azure AI Search adapter, bound from the
/// <c>Azure:Search</c> section.
/// </summary>
/// <remarks>
/// <para>
/// <b>No API key.</b> Azure AI Search issues admin and query keys, and neither is
/// used here. Authentication is Entra ID via <c>DefaultAzureCredential</c>, so
/// the only configuration needed is where the service is and which index to
/// write to — neither of which is a credential. The credential itself is
/// configured once in <c>Azure:Credential</c>, shared with every other Azure
/// client.
/// </para>
/// <para>
/// <b>SemanticConfigurationName is deliberately not bound.</b> It exists in
/// <c>appsettings.json</c> for a later sprint. Binding a setting this slice does
/// not honour would imply a capability that is not implemented.
/// </para>
/// </remarks>
public sealed class AzureSearchOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Azure:Search";

    /// <summary>
    /// The search service endpoint, for example
    /// <c>https://contoso.search.windows.net/</c>.
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Azure:Search:Endpoint must be configured.")]
    [Url(ErrorMessage = "Azure:Search:Endpoint must be an absolute URL.")]
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>The index document metadata is written to.</summary>
    /// <remarks>
    /// The pattern is Azure AI Search's own index naming rule — 2 to 128
    /// characters of lowercase letters, digits, and dashes, not starting or
    /// ending with a dash. Validating it here turns a typo from a 400 on the
    /// first upload into a startup failure that names the setting.
    /// </remarks>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Azure:Search:IndexName must be configured.")]
    [RegularExpression(
        "^[a-z0-9][a-z0-9-]{0,126}[a-z0-9]$",
        ErrorMessage = "Azure:Search:IndexName must be 2-128 characters of lowercase letters, digits, and dashes, and may not start or end with a dash.")]
    public string IndexName { get; init; } = string.Empty;

    /// <summary>The index chunks and their vectors are written to.</summary>
    /// <remarks>
    /// <para>
    /// <b>A second index, not a variant of the first.</b> <see cref="IndexName"/>
    /// holds one record per document, keyed by document id, and answers "which
    /// documents exist". This one holds one record per chunk, keyed by chunk id,
    /// and answers "which passages match". An index has exactly one key, so the
    /// two granularities cannot share one — and re-keying the existing index
    /// would mean dropping and reloading it.
    /// </para>
    /// </remarks>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Azure:Search:ChunkIndexName must be configured.")]
    [RegularExpression(
        "^[a-z0-9][a-z0-9-]{0,126}[a-z0-9]$",
        ErrorMessage = "Azure:Search:ChunkIndexName must be 2-128 characters of lowercase letters, digits, and dashes, and may not start or end with a dash.")]
    public string ChunkIndexName { get; init; } = "knowledge-chunks";

    /// <summary>The number of chunks written in a single indexing request.</summary>
    /// <remarks>
    /// <para>
    /// Azure caps a batch at 1000 documents, but that is not the limit that
    /// binds here — the 16 MB request ceiling is. A 3072-dimension vector
    /// (text-embedding-3-large) serialises to roughly 40 KB of JSON, so a
    /// thousand chunks would be about 40 MB and be rejected outright. A hundred
    /// keeps a batch near 4 MB with room for text, which is why the default is
    /// well under the documented maximum.
    /// </para>
    /// </remarks>
    [Range(1, 1000, ErrorMessage = "Azure:Search:IndexingBatchSize must be between 1 and 1000.")]
    public int IndexingBatchSize { get; init; } = 100;

    /// <summary>Bi-directional links per node in the HNSW graph.</summary>
    /// <remarks>
    /// Higher values improve recall and enlarge the graph. <b>Fixed once the
    /// index exists</b> — changing it requires a new index and a full reload.
    /// </remarks>
    [Range(4, 10, ErrorMessage = "Azure:Search:HnswM must be between 4 and 10.")]
    public int HnswM { get; init; } = 4;

    /// <summary>Candidate list size used while building the HNSW graph.</summary>
    /// <remarks>
    /// Costs index-build time only and nothing at query time. Also fixed at
    /// index creation.
    /// </remarks>
    [Range(100, 1000, ErrorMessage = "Azure:Search:HnswEfConstruction must be between 100 and 1000.")]
    public int HnswEfConstruction { get; init; } = 400;

    /// <summary>Candidate list size used while searching.</summary>
    /// <remarks>
    /// The one HNSW parameter that can be revised on an existing index. Raising
    /// it trades query latency for recall.
    /// </remarks>
    [Range(100, 1000, ErrorMessage = "Azure:Search:HnswEfSearch must be between 100 and 1000.")]
    public int HnswEfSearch { get; init; } = 500;

    /// <summary>
    /// Whether the index declares an Azure OpenAI vectorizer for query-time
    /// embedding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Off by design, not used</b> — <c>false</c> when the key is absent.
    /// The application embeds every query itself through
    /// <c>IEmbeddingService</c> and submits a <c>VectorizedQuery</c>, so
    /// query and corpus vectors always come from the same configured model. A
    /// service-side vectorizer would be a second, independently configured path
    /// to the same result.
    /// </para>
    /// <para>
    /// Leaving it off also means the search service needs no managed identity
    /// and no role on the AI Foundry resource. Were it ever enabled, that call
    /// runs as the <i>search service's</i> identity, not this application's, and
    /// a vectorizer cannot be added to an existing index without a rebuild.
    /// </para>
    /// </remarks>
    public bool EnableVectorizer { get; init; }
}
