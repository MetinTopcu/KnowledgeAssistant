using Azure.Search.Documents.Indexes.Models;
using KnowledgeAssistant.Infrastructure.Azure.OpenAI;

namespace KnowledgeAssistant.Infrastructure.Search.Vectors;

/// <summary>
/// Defines the retrieval index: its fields and its vector search configuration.
/// </summary>
/// <remarks>
/// <para>
/// Code-defined and rebuildable, per <c>Search/README.md</c>. Field names are
/// taken with <c>nameof</c> from <see cref="ChunkSearchDocument"/>, so the schema
/// and the documents written against it cannot drift apart.
/// </para>
/// <para>
/// <b>Almost nothing here can be changed after the index exists.</b> Vector
/// dimensions, the HNSW graph parameters <c>m</c> and <c>efConstruction</c>, and
/// a field's stored-ness are fixed at creation: altering them requires a new
/// index and a full reload. That is why the adapter creates this index and never
/// updates it, and why a change to any of these values should be treated as a
/// corpus rebuild rather than a configuration tweak.
/// </para>
/// </remarks>
internal static class ChunkIndexSchema
{
    /// <summary>The name of the HNSW algorithm configuration.</summary>
    internal const string AlgorithmConfigurationName = "hnsw-default";

    /// <summary>The name of the vector search profile fields point at.</summary>
    internal const string ProfileName = "vector-profile-default";

    /// <summary>The name of the Azure OpenAI vectorizer, when one is configured.</summary>
    internal const string VectorizerName = "azure-openai-vectorizer";

    /// <summary>Builds the index definition.</summary>
    /// <param name="indexName">The index to create.</param>
    /// <param name="searchOptions">Vector search tuning.</param>
    /// <param name="openAIOptions">
    /// Supplies the embedding dimensions and, when the vectorizer is enabled, the
    /// resource and deployment it should call.
    /// </param>
    internal static SearchIndex Build(
        string indexName,
        AzureSearchOptions searchOptions,
        AzureOpenAIOptions openAIOptions)
    {
        var fields = new List<SearchField>
        {
            // The key. Filterable so a specific chunk can be addressed directly.
            new(nameof(ChunkSearchDocument.ChunkId), SearchFieldDataType.String)
            {
                IsKey = true,
                IsFilterable = true,
            },

            // The grouping field: every operation that treats a document as a unit
            // — re-index, delete, count — filters on this.
            new(nameof(ChunkSearchDocument.DocumentId), SearchFieldDataType.String)
            {
                IsFilterable = true,
                IsSortable = true,
            },

            new(nameof(ChunkSearchDocument.ChunkOrder), SearchFieldDataType.Int32)
            {
                IsFilterable = true,
                IsSortable = true,
            },

            // Retrievable and searchable: a retrieval hit has to be able to hand
            // back the text it matched.
            new(nameof(ChunkSearchDocument.ChunkText), SearchFieldDataType.String)
            {
                IsSearchable = true,
            },

            // Neither filterable nor searchable — a pointer back to the source,
            // returned with a hit and never queried on.
            new(nameof(ChunkSearchDocument.BlobUri), SearchFieldDataType.String),

            new(nameof(ChunkSearchDocument.UploadedAt), SearchFieldDataType.DateTimeOffset)
            {
                IsFilterable = true,
                IsSortable = true,
                IsFacetable = true,
            },

            // The vector field.
            new(nameof(ChunkSearchDocument.Embedding), SearchFieldDataType.Collection(SearchFieldDataType.Single))
            {
                // Required: a vector field that is not searchable is just storage.
                IsSearchable = true,

                // Hidden and not stored. A caller never needs the raw vector back
                // — it is matched against, not read — and suppressing the stored
                // copy roughly halves what the index costs to hold. Both settings
                // are fixed once the index exists; making the vector retrievable
                // later means a new index.
                IsHidden = true,
                IsStored = false,

                // From configuration, which is the reason this schema is built in
                // code rather than derived from attributes on the model.
                VectorSearchDimensions = openAIOptions.EmbeddingDimensions,
                VectorSearchProfileName = ProfileName,
            },
        };

        var vectorSearch = new VectorSearch
        {
            Algorithms =
            {
                new HnswAlgorithmConfiguration(AlgorithmConfigurationName)
                {
                    Parameters = new HnswParameters
                    {
                        // Cosine, because Azure OpenAI embeddings are normalised —
                        // for unit-length vectors cosine and dot product rank
                        // identically, and cosine is what the models document.
                        Metric = VectorSearchAlgorithmMetric.Cosine,

                        // Bi-directional links per node. Higher means better recall
                        // and a larger, slower-to-build graph. Fixed at creation.
                        M = searchOptions.HnswM,

                        // Candidate list size while building. Raising it improves
                        // graph quality at index time only; it costs nothing at
                        // query time. Also fixed at creation.
                        EfConstruction = searchOptions.HnswEfConstruction,

                        // Candidate list size while searching — the one knob here
                        // that can be raised later to trade latency for recall.
                        EfSearch = searchOptions.HnswEfSearch,
                    },
                },
            },
            Profiles =
            {
                new VectorSearchProfile(ProfileName, AlgorithmConfigurationName),
            },
        };

        if (searchOptions.EnableVectorizer)
        {
            // Integrated vectorization: it lets the service turn a query string
            // into a vector at query time, so a caller does not have to embed the
            // query itself. Nothing in this slice uses it — there are no queries
            // yet — but it is part of the index definition, and the index cannot
            // be given one later without being rebuilt.
            //
            // It executes as the *search service's* identity, not this
            // application's, so it needs its own role assignment on the OpenAI
            // resource. Configuring it here does not grant that.
            vectorSearch.Vectorizers.Add(new AzureOpenAIVectorizer(VectorizerName)
            {
                Parameters = new AzureOpenAIVectorizerParameters
                {
                    ResourceUri = ResolveUri(openAIOptions.Endpoint),
                    DeploymentName = openAIOptions.EmbeddingDeploymentName,

                    // The service requires the underlying model, not just the
                    // deployment: it validates that the dimensions declared above
                    // are achievable by that model before accepting the index.
                    ModelName = new AzureOpenAIModelName(openAIOptions.EmbeddingModelName),
                },
            });

            vectorSearch.Profiles[0].VectorizerName = VectorizerName;
        }

        return new SearchIndex(indexName, fields)
        {
            VectorSearch = vectorSearch,
        };
    }

    private static Uri? ResolveUri(string endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) ? uri : null;
}
