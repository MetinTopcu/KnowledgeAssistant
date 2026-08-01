using Azure;
using KnowledgeAssistant.Application.Interfaces;
using KnowledgeAssistant.Domain.Common;
using KnowledgeAssistant.Infrastructure.Azure.OpenAI;
using KnowledgeAssistant.Infrastructure.Search;
using KnowledgeAssistant.Infrastructure.Search.Vectors;
using KnowledgeAssistant.Tests.Unit.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KnowledgeAssistant.Tests.Unit.Search;

/// <summary>
/// The vector indexing adapter: batching, index provisioning, and rejection detection.
/// </summary>
/// <remarks>
/// <para>
/// The assertions about partial failure are the reason this file is long. Azure
/// AI Search answers <c>200 OK</c> for a batch in which individual documents were
/// rejected — throttled, malformed, too large — and an adapter that checks only
/// the HTTP status reports a fully indexed document that is missing chunks. The
/// symptom appears much later, as an answer that cites nothing useful, with
/// nothing in the logs to connect it to this upload.
/// </para>
/// <para>
/// Driven through a faked <c>SearchClient</c>, so those responses are produced on
/// demand rather than waited for.
/// </para>
/// </remarks>
public sealed class AzureSearchVectorIndexServiceTests
{
    private static AzureSearchOptions SearchOptions(int indexingBatchSize = 100) => new()
    {
        Endpoint = "https://fake.search.windows.net/",
        IndexName = "knowledge-index",
        ChunkIndexName = "knowledge-chunks",
        IndexingBatchSize = indexingBatchSize,
        HnswM = 4,
        HnswEfConstruction = 400,
        HnswEfSearch = 500,
        EnableVectorizer = true,
    };

    private static AzureOpenAIOptions OpenAIOptions(int dimensions = 1536) => new()
    {
        Endpoint = "https://fake.services.ai.azure.com/",
        EmbeddingDeploymentName = "my-embed-deployment",
        EmbeddingModelName = "text-embedding-3-small",
        ChatDeploymentName = "gpt-4o-mini",
        EmbeddingDimensions = dimensions,
    };

    private static AzureSearchVectorIndexService Create(
        FakeSearchIndexClient indexClient,
        AzureSearchOptions? searchOptions = null,
        AzureOpenAIOptions? openAIOptions = null) =>
        new(
            indexClient,
            Options.Create(searchOptions ?? SearchOptions()),
            Options.Create(openAIOptions ?? OpenAIOptions()),
            NullLogger<AzureSearchVectorIndexService>.Instance);

    private static VectorIndexRequest Request(int chunkCount, int dimensions = 1536) => new(
        Guid.CreateVersion7(),
        new Uri("https://acct.blob.core.windows.net/documents/2026/08/01/doc.pdf"),
        new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
        [.. Enumerable.Range(0, chunkCount).Select(index =>
            new VectorIndexChunk(Guid.CreateVersion7(), index, $"chunk {index}", new float[dimensions]))]);

    [Fact]
    public async Task IndexChunks_SplitsTheDocumentIntoConfiguredBatches()
    {
        var searchClient = new FakeSearchClient();
        using AzureSearchVectorIndexService service =
            Create(new FakeSearchIndexClient(searchClient), SearchOptions(indexingBatchSize: 100));

        Result result = await service.IndexChunksAsync(Request(250), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        searchClient.BatchSizes.Should().Equal(100, 100, 50);
        searchClient.AllDocuments.Should().HaveCount(250, "every chunk is sent exactly once");
    }

    [Fact]
    public async Task IndexChunks_CreatesTheChunkIndexOnFirstUse()
    {
        var indexClient = new FakeSearchIndexClient(new FakeSearchClient());
        using AzureSearchVectorIndexService service = Create(indexClient);

        await service.IndexChunksAsync(Request(3), CancellationToken.None);

        indexClient.CreateCallCount.Should().Be(1);
        indexClient.CreatedIndex!.Name.Should().Be("knowledge-chunks",
            "the vector adapter provisions the chunk index, not the document index");
    }

    [Fact]
    public async Task IndexChunks_CachesIndexExistenceAcrossCalls()
    {
        // Otherwise every batch of every upload pays a round trip to ask a
        // question whose answer cannot change back.
        var indexClient = new FakeSearchIndexClient(new FakeSearchClient());
        using AzureSearchVectorIndexService service = Create(indexClient);

        await service.IndexChunksAsync(Request(3), CancellationToken.None);
        await service.IndexChunksAsync(Request(3), CancellationToken.None);

        indexClient.GetCallCount.Should().Be(1);
    }

    [Fact]
    public async Task IndexChunks_WhenTheIndexAlreadyExists_DoesNotRecreateIt()
    {
        var indexClient = new FakeSearchIndexClient(new FakeSearchClient()) { IndexExists = true };
        using AzureSearchVectorIndexService service = Create(indexClient);

        await service.IndexChunksAsync(Request(3), CancellationToken.None);

        indexClient.CreateCallCount.Should().Be(0, "recreating an index would drop the corpus");
    }

    [Fact]
    public async Task IndexChunks_SendsMetadataTextAndVectorInOneOperation()
    {
        // One write per chunk. A separate metadata pass and vector pass would make
        // every chunk briefly retrievable without its embedding, and permanently
        // so if the second pass failed.
        var searchClient = new FakeSearchClient();
        using AzureSearchVectorIndexService service =
            Create(new FakeSearchIndexClient(searchClient), openAIOptions: OpenAIOptions(dimensions: 8));
        VectorIndexRequest request = Request(2, dimensions: 8);

        await service.IndexChunksAsync(request, CancellationToken.None);

        searchClient.BatchSizes.Should().ContainSingle();

        var document = searchClient.AllDocuments[0].Should().BeOfType<ChunkSearchDocument>().Subject;

        document.ChunkId.Should().Be(request.Chunks[0].ChunkId.ToString());
        document.DocumentId.Should().Be(request.DocumentId.ToString());
        document.ChunkOrder.Should().Be(0);
        document.ChunkText.Should().Be("chunk 0");
        document.BlobUri.Should().Be(request.BlobUri.AbsoluteUri);
        document.UploadedAt.Should().Be(request.UploadedAt);
        document.Embedding.Should().HaveCount(8);
    }

    [Fact]
    public async Task IndexChunks_DenormalisesDocumentMetadataOntoEveryChunk()
    {
        // The chunk index is queried alone, so a chunk that cannot name its own
        // document cannot be cited or deleted with the rest of it.
        var searchClient = new FakeSearchClient();
        using AzureSearchVectorIndexService service = Create(new FakeSearchIndexClient(searchClient));
        VectorIndexRequest request = Request(5);

        await service.IndexChunksAsync(request, CancellationToken.None);

        searchClient.AllDocuments.Cast<ChunkSearchDocument>()
            .Should().OnlyContain(document => document.DocumentId == request.DocumentId.ToString());
    }

    [Fact]
    public async Task IndexChunks_WhenAVectorHasTheWrongLength_FailsBeforeSendingAnything()
    {
        // A dimension mismatch would be rejected by the service anyway. Catching
        // it first turns a partially applied batch into a clean, local failure.
        var searchClient = new FakeSearchClient();
        using AzureSearchVectorIndexService service = Create(new FakeSearchIndexClient(searchClient));

        VectorIndexRequest valid = Request(3);
        var malformed = new VectorIndexRequest(
            valid.DocumentId,
            valid.BlobUri,
            valid.UploadedAt,
            [valid.Chunks[0], new VectorIndexChunk(Guid.CreateVersion7(), 1, "bad", new float[100]), valid.Chunks[2]]);

        Result result = await service.IndexChunksAsync(malformed, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("VectorIndex.DimensionMismatch");
        searchClient.BatchSizes.Should().BeEmpty();
    }

    [Fact]
    public async Task IndexChunks_WhenTheDimensionCheckIsDisabled_AcceptsAnyVectorLength()
    {
        // Configuring zero dimensions is how a deployment opts out — for a model
        // whose output size is not known ahead of time.
        using AzureSearchVectorIndexService service = Create(
            new FakeSearchIndexClient(new FakeSearchClient()),
            openAIOptions: OpenAIOptions(dimensions: 0));

        Result result = await service.IndexChunksAsync(Request(3, dimensions: 77), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task IndexChunks_WhenIndividualDocumentsAreRejectedInsideASuccessfulResponse_Fails()
    {
        // The case this adapter exists for: HTTP 200, and two chunks throttled
        // inside it. Trusting the status code silently loses them.
        var searchClient = new FakeSearchClient { FailuresInNextBatch = 2 };
        using AzureSearchVectorIndexService service = Create(new FakeSearchIndexClient(searchClient));

        Result result = await service.IndexChunksAsync(Request(5), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("VectorIndex.ChunksRejected");
    }

    [Fact]
    public async Task IndexChunks_WhenTheResponseReportsFewerResultsThanDocumentsSent_Fails()
    {
        // A short result list is not a success with missing detail; it is chunks
        // whose fate is unknown, which must be treated as failure.
        var searchClient = new FakeSearchClient { TruncateResultsTo = 3 };
        using AzureSearchVectorIndexService service = Create(new FakeSearchIndexClient(searchClient));

        Result result = await service.IndexChunksAsync(Request(5), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("VectorIndex.ChunksRejected");
    }

    [Fact]
    public async Task IndexChunks_WhenTheServiceThrows_ReportsIndexingFailedRatherThanPropagating()
    {
        var searchClient = new FakeSearchClient { ThrowNext = new RequestFailedException(503, "unavailable") };
        using AzureSearchVectorIndexService service = Create(new FakeSearchIndexClient(searchClient));

        Result result = await service.IndexChunksAsync(Request(3), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("VectorIndex.IndexingFailed",
            "an SDK exception must not escape the adapter as an unhandled fault");
    }

    [Fact]
    public async Task IndexChunks_WithNoChunks_SucceedsWithoutProvisioningOrSending()
    {
        // An empty document is not an error, and it must not create an index or
        // spend a request to say so.
        var searchClient = new FakeSearchClient();
        var indexClient = new FakeSearchIndexClient(searchClient);
        using AzureSearchVectorIndexService service = Create(indexClient);

        var empty = new VectorIndexRequest(
            Guid.CreateVersion7(),
            new Uri("https://acct.blob.core.windows.net/documents/empty.pdf"),
            DateTimeOffset.UtcNow,
            []);

        Result result = await service.IndexChunksAsync(empty, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        searchClient.BatchSizes.Should().BeEmpty();
        indexClient.CreateCallCount.Should().Be(0);
    }

    [Fact]
    public async Task IndexChunks_AttemptsEveryBatchOfAMultiBatchDocument()
    {
        var searchClient = new FakeSearchClient();
        using AzureSearchVectorIndexService service =
            Create(new FakeSearchIndexClient(searchClient), SearchOptions(indexingBatchSize: 10));

        Result result = await service.IndexChunksAsync(Request(25), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        searchClient.BatchSizes.Should().Equal(10, 10, 5);
    }
}
