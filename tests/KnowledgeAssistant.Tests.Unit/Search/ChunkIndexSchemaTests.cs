using Azure.Search.Documents.Indexes.Models;
using KnowledgeAssistant.Infrastructure.Azure.OpenAI;
using KnowledgeAssistant.Infrastructure.Search;
using KnowledgeAssistant.Infrastructure.Search.Vectors;

namespace KnowledgeAssistant.Tests.Unit.Search;

/// <summary>
/// The chunk index definition: fields, vector configuration, and HNSW tuning.
/// </summary>
/// <remarks>
/// <para>
/// Worth testing precisely because an index schema is close to immutable in
/// practice. Azure AI Search will not change a field's type or dimensions in
/// place, so a mistake here is not a bug fix later — it is a re-index of the
/// entire corpus, at the cost of embedding every chunk again.
/// </para>
/// <para>
/// The schema is built as a value and inspected, so none of this needs a search
/// service to exist.
/// </para>
/// </remarks>
public sealed class ChunkIndexSchemaTests
{
    private static AzureSearchOptions SearchOptions(
        bool enableVectorizer = true,
        int hnswM = 4,
        int efConstruction = 400,
        int efSearch = 500) => new()
    {
        Endpoint = "https://fake.search.windows.net/",
        IndexName = "knowledge-index",
        ChunkIndexName = "knowledge-chunks",
        IndexingBatchSize = 100,
        HnswM = hnswM,
        HnswEfConstruction = efConstruction,
        HnswEfSearch = efSearch,
        EnableVectorizer = enableVectorizer,
    };

    private static AzureOpenAIOptions OpenAIOptions(int dimensions = 1536) => new()
    {
        Endpoint = "https://fake.services.ai.azure.com/",
        EmbeddingDeploymentName = "my-embed-deployment",
        EmbeddingModelName = "text-embedding-3-small",
        ChatDeploymentName = "gpt-4o-mini",
        EmbeddingDimensions = dimensions,
    };

    private static SearchIndex Build(
        AzureSearchOptions? searchOptions = null,
        AzureOpenAIOptions? openAIOptions = null)
    {
        AzureSearchOptions search = searchOptions ?? SearchOptions();
        return ChunkIndexSchema.Build(search.ChunkIndexName, search, openAIOptions ?? OpenAIOptions());
    }

    [Fact]
    public void Build_DefinesExactlyTheSevenExpectedFields()
    {
        // "No unexpected fields" matters as much as the positive check: a stray
        // field is storage paid for on every chunk of every document, forever.
        IEnumerable<string> names = Build().Fields.Select(field => field.Name);

        names.Should().BeEquivalentTo(
            "ChunkId", "DocumentId", "ChunkOrder", "ChunkText", "BlobUri", "UploadedAt", "Embedding");
    }

    [Fact]
    public void Build_MakesChunkIdTheSingleStringKey()
    {
        SearchIndex index = Build();

        index.Fields.Should().ContainSingle(field => field.IsKey == true)
            .Which.Name.Should().Be("ChunkId");
        index.Fields.Single(field => field.Name == "ChunkId").Type.Should().Be(SearchFieldDataType.String);
    }

    [Fact]
    public void Build_MakesDocumentIdFilterableSoChunksCanBeGroupedByDocument()
    {
        // Every operation that treats a document as a unit — re-index, delete,
        // count — is a filter on this field. Without it they are impossible.
        Field(Build(), "DocumentId").IsFilterable.Should().BeTrue();
    }

    [Fact]
    public void Build_MakesChunkOrderASortableInt32()
    {
        SearchField chunkOrder = Field(Build(), "ChunkOrder");

        chunkOrder.Type.Should().Be(SearchFieldDataType.Int32);
        chunkOrder.IsSortable.Should().BeTrue("chunks are reassembled in document order");
    }

    [Fact]
    public void Build_MakesChunkTextSearchableAndRetrievable()
    {
        SearchField chunkText = Field(Build(), "ChunkText");

        chunkText.IsSearchable.Should().BeTrue();
        chunkText.IsHidden.Should().NotBe(true, "a result that cannot return its own text cannot be cited");
    }

    [Fact]
    public void Build_MakesUploadedAtAFilterableSortableTimestamp()
    {
        SearchField uploadedAt = Field(Build(), "UploadedAt");

        uploadedAt.Type.Should().Be(SearchFieldDataType.DateTimeOffset);
        uploadedAt.IsFilterable.Should().BeTrue();
        uploadedAt.IsSortable.Should().BeTrue();
    }

    [Fact]
    public void Build_ConfiguresTheEmbeddingFieldAsAHiddenUnstoredVector()
    {
        SearchField embedding = Field(Build(), "Embedding");

        embedding.Type.Should().Be(SearchFieldDataType.Collection(SearchFieldDataType.Single));
        embedding.IsSearchable.Should().BeTrue("a vector field must be searchable to be queried");
        embedding.IsHidden.Should().BeTrue("callers get chunk text, never raw vectors");
        embedding.IsStored.Should().BeFalse("not storing the retrievable copy halves vector storage");
        embedding.IsFilterable.Should().NotBe(true);
        embedding.IsFacetable.Should().NotBe(true);
    }

    [Theory]
    [InlineData(1536)]
    [InlineData(3072)]
    public void Build_TakesVectorDimensionsFromConfiguration(int dimensions)
    {
        // The setting that cannot be changed after the fact: a mismatch between
        // this and the embedding deployment rejects every document at index time.
        Field(Build(openAIOptions: OpenAIOptions(dimensions)), "Embedding")
            .VectorSearchDimensions.Should().Be(dimensions);
    }

    [Fact]
    public void Build_PointsTheVectorFieldAtAProfileThatExists()
    {
        // A dangling profile name is accepted by the model object and rejected by
        // the service, so the only cheap place to catch it is here.
        SearchIndex index = Build();

        string profileName = Field(index, "Embedding").VectorSearchProfileName;

        index.VectorSearch.Profiles.Should().ContainSingle(profile => profile.Name == profileName);
    }

    [Fact]
    public void Build_ConfiguresOneCosineHnswAlgorithmFromConfiguration()
    {
        SearchIndex index = Build(SearchOptions(hnswM: 8, efConstruction: 500, efSearch: 700));

        index.VectorSearch.Algorithms.Should().ContainSingle();

        var hnsw = index.VectorSearch.Algorithms[0].Should().BeOfType<HnswAlgorithmConfiguration>().Subject;

        hnsw.Name.Should().Be(ChunkIndexSchema.AlgorithmConfigurationName);
        hnsw.Parameters.Metric.Should().Be(VectorSearchAlgorithmMetric.Cosine,
            "the embedding model is trained for cosine similarity");
        hnsw.Parameters.M.Should().Be(8);
        hnsw.Parameters.EfConstruction.Should().Be(500);
        hnsw.Parameters.EfSearch.Should().Be(700);
    }

    [Fact]
    public void Build_LinksTheProfileToTheAlgorithm()
    {
        SearchIndex index = Build();

        index.VectorSearch.Profiles.Should().ContainSingle()
            .Which.AlgorithmConfigurationName.Should().Be(ChunkIndexSchema.AlgorithmConfigurationName);
    }

    [Fact]
    public void Build_WhenTheVectorizerIsEnabled_PointsItAtTheEmbeddingDeploymentWithoutAKey()
    {
        SearchIndex index = Build(SearchOptions(enableVectorizer: true));

        var vectorizer = index.VectorSearch.Vectorizers.Should().ContainSingle()
            .Which.Should().BeOfType<AzureOpenAIVectorizer>().Subject;

        vectorizer.VectorizerName.Should().Be(ChunkIndexSchema.VectorizerName);
        vectorizer.Parameters.ResourceUri.Should().Be(new Uri("https://fake.services.ai.azure.com/"));
        vectorizer.Parameters.DeploymentName.Should().Be("my-embed-deployment",
            "the vectorizer calls a deployment, not a model");
        vectorizer.Parameters.ModelName.Should().Be(AzureOpenAIModelName.TextEmbedding3Small,
            "the service validates the model name against the deployment");
        vectorizer.Parameters.ApiKey.Should().BeNull("authentication is managed identity; no key should exist");
        index.VectorSearch.Profiles[0].VectorizerName.Should().Be(vectorizer.VectorizerName);
    }

    [Fact]
    public void Build_WhenTheVectorizerIsDisabled_StillProducesAUsableVectorIndex()
    {
        // Disabling integrated vectorization removes query-time text-to-vector
        // convenience. It must not remove the ability to search by vector, which
        // is what this application actually does.
        SearchIndex index = Build(SearchOptions(enableVectorizer: false));

        index.VectorSearch.Vectorizers.Should().BeEmpty();
        index.VectorSearch.Profiles[0].VectorizerName.Should().BeNull();
        index.VectorSearch.Algorithms.Should().ContainSingle();
        index.VectorSearch.Profiles.Should().ContainSingle();
    }

    private static SearchField Field(SearchIndex index, string name) =>
        index.Fields.Single(field => field.Name == name);
}
