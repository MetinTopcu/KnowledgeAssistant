using Azure;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using KnowledgeAssistant.Application.Interfaces;
using KnowledgeAssistant.Domain.Common;
using KnowledgeAssistant.Infrastructure.Search;
using KnowledgeAssistant.Infrastructure.Search.Vectors;
using KnowledgeAssistant.Tests.Unit.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

// The adapter's typed record, not the SDK's dictionary of the same name.
using IndexedDocument = KnowledgeAssistant.Infrastructure.Search.SearchDocument;

namespace KnowledgeAssistant.Tests.Unit.Search;

/// <summary>
/// The document-index writer and the retrieval query: what is sent to Azure AI
/// Search, which index it goes to, and how every outcome is reported.
/// </summary>
/// <remarks>
/// <para>
/// <b>Driven through faked SDK clients, not a faked port.</b> The adapter builds
/// real <see cref="SearchOptions"/>, real <see cref="VectorizedQuery"/> objects and
/// real <see cref="IndexedDocument"/> payloads, and enumerates a genuine
/// <see cref="SearchResults{T}"/> made by the SDK's model factory. Only the network
/// hop is replaced. The HTTP request that options object becomes is asserted
/// separately, in <see cref="AzureSearchServiceTransportTests"/>.
/// </para>
/// <para>
/// Every index has its own client here, so a query that reached the document index,
/// or a write that reached the chunk index, fails an assertion instead of passing
/// silently against a shared fake.
/// </para>
/// </remarks>
public sealed class AzureSearchServiceTests
{
    private const string DocumentIndex = "knowledge-index";
    private const string ChunkIndex = "knowledge-chunks";

    private sealed record Rig(
        AzureSearchService Service,
        FakeSearchIndexClient IndexClient,
        FakeSearchClient DocumentClient,
        FakeSearchClient ChunkClient) : IDisposable
    {
        public void Dispose() => Service.Dispose();
    }

    private static Rig CreateRig(bool indexExists = false)
    {
        var documentClient = new FakeSearchClient();
        var chunkClient = new FakeSearchClient();

        // The default client is neither index's: anything routed to it is a bug.
        var indexClient = new FakeSearchIndexClient(new FakeSearchClient()) { IndexExists = indexExists };
        indexClient.ClientsByIndex[DocumentIndex] = documentClient;
        indexClient.ClientsByIndex[ChunkIndex] = chunkClient;

        var service = new AzureSearchService(
            indexClient,
            Options.Create(new AzureSearchOptions
            {
                Endpoint = "https://fake.search.windows.net/",
                IndexName = DocumentIndex,
                ChunkIndexName = ChunkIndex,
            }),
            NullLogger<AzureSearchService>.Instance);

        return new Rig(service, indexClient, documentClient, chunkClient);
    }

    private static readonly float[] QueryVector = [0.5f, -0.25f, 1f];

    private static SearchResult<ChunkSearchDocument> Hit(
        Guid? chunkId = null,
        Guid? documentId = null,
        int chunkOrder = 0,
        string text = "passage",
        string blobUri = "https://acct.blob.core.windows.net/documents/2026/08/01/doc.pdf",
        double? score = 0.9,
        string? rawChunkId = null,
        string? rawDocumentId = null) =>
        SearchModelFactory.SearchResult(
            new ChunkSearchDocument
            {
                ChunkId = rawChunkId ?? (chunkId ?? Guid.CreateVersion7()).ToString(),
                DocumentId = rawDocumentId ?? (documentId ?? Guid.CreateVersion7()).ToString(),
                ChunkOrder = chunkOrder,
                ChunkText = text,
                BlobUri = blobUri,
            },
            score,
            highlights: null);

    private static DocumentIndexRequest DocumentRequest() => new(
        DocumentId: Guid.CreateVersion7(),
        BlobName: "2026/08/01/0198a1b2-0000-7000-8000-000000000001.pdf",
        OriginalFileName: "rapor-şubat.pdf",
        BlobUri: new Uri("https://acct.blob.core.windows.net/documents/2026/08/01/0198a1b2-0000-7000-8000-000000000001.pdf"),
        UploadedAt: new DateTimeOffset(2026, 8, 1, 12, 30, 0, TimeSpan.Zero));

    // ---- SearchChunksAsync: the query --------------------------------------

    [Fact]
    public void Constructor_DerivesOneClientPerIndexFromTheSharedIndexClient()
    {
        // One pipeline, one credential, one token cache: both clients come from the
        // registered SearchIndexClient rather than being built separately.
        using Rig rig = CreateRig();

        rig.IndexClient.RequestedClientNames.Should().Equal(DocumentIndex, ChunkIndex);
    }

    [Fact]
    public async Task SearchChunks_SendsAPureVectorQueryToTheChunkIndex()
    {
        using Rig rig = CreateRig();

        await rig.Service.SearchChunksAsync(QueryVector, topK: 5, CancellationToken.None);

        rig.ChunkClient.SearchCallCount.Should().Be(1);
        rig.DocumentClient.SearchCallCount.Should().Be(0, "retrieval reads chunks, never the document index");

        rig.ChunkClient.LastSearchText.Should().BeNull("a null search text is what keeps this from being a hybrid query");

        SearchOptions options = rig.ChunkClient.LastSearchOptions!;
        options.QueryType.Should().BeNull("no keyword or semantic query type is requested");
        options.SemanticSearch.Should().BeNull("semantic ranking is not part of this retrieval");

        VectorizedQuery query = options.VectorSearch.Queries.Should().ContainSingle()
            .Which.Should().BeOfType<VectorizedQuery>().Subject;

        query.Vector.ToArray().Should().Equal(QueryVector, "the caller's embedding is sent unchanged");
    }

    [Fact]
    public async Task SearchChunks_TargetsTheEmbeddingField()
    {
        using Rig rig = CreateRig();

        await rig.Service.SearchChunksAsync(QueryVector, topK: 5, CancellationToken.None);

        VectorizedQuery query = (VectorizedQuery)rig.ChunkClient.LastSearchOptions!.VectorSearch.Queries[0];

        query.Fields.Should().Equal(nameof(ChunkSearchDocument.Embedding));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(20)]
    public async Task SearchChunks_UsesTopKForBothTheNeighbourCountAndThePageSize(int topK)
    {
        // Both, deliberately: k bounds the vector search, Size bounds the response.
        // Without Size the service applies its own default page size.
        using Rig rig = CreateRig();

        await rig.Service.SearchChunksAsync(QueryVector, topK, CancellationToken.None);

        SearchOptions options = rig.ChunkClient.LastSearchOptions!;
        VectorizedQuery query = (VectorizedQuery)options.VectorSearch.Queries[0];

        query.KNearestNeighborsCount.Should().Be(topK);
        options.Size.Should().Be(topK);
    }

    [Fact]
    public async Task SearchChunks_SelectsExactlyTheFieldsACitationNeeds()
    {
        using Rig rig = CreateRig();

        await rig.Service.SearchChunksAsync(QueryVector, topK: 5, CancellationToken.None);

        rig.ChunkClient.LastSearchOptions!.Select.Should().Equal(
            nameof(ChunkSearchDocument.ChunkId),
            nameof(ChunkSearchDocument.DocumentId),
            nameof(ChunkSearchDocument.ChunkOrder),
            nameof(ChunkSearchDocument.ChunkText),
            nameof(ChunkSearchDocument.BlobUri));
    }

    [Fact]
    public async Task SearchChunks_DoesNotProvisionOrInspectAnyIndex()
    {
        // Creating the chunk index is the vector adapter's job. A read must not
        // create somewhere for content that by definition is not there.
        using Rig rig = CreateRig();

        await rig.Service.SearchChunksAsync(QueryVector, topK: 5, CancellationToken.None);

        rig.IndexClient.GetCallCount.Should().Be(0);
        rig.IndexClient.CreateCallCount.Should().Be(0);
    }

    [Fact]
    public async Task SearchChunks_PassesTheCallersCancellationToken()
    {
        // A disconnecting caller must be able to abandon the query.
        using Rig rig = CreateRig();
        using var cancellation = new CancellationTokenSource();

        await rig.Service.SearchChunksAsync(QueryVector, topK: 5, cancellation.Token);

        rig.ChunkClient.LastSearchToken.Should().Be(cancellation.Token);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task SearchChunks_WithANonPositiveTopK_ThrowsWithoutCallingTheService(int topK)
    {
        using Rig rig = CreateRig();

        Func<Task> act = () => rig.Service.SearchChunksAsync(QueryVector, topK, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        rig.ChunkClient.SearchCallCount.Should().Be(0);
    }

    [Fact]
    public async Task SearchChunks_WithAnEmptyVector_ThrowsWithoutCallingTheService()
    {
        using Rig rig = CreateRig();

        Func<Task> act = () => rig.Service.SearchChunksAsync(ReadOnlyMemory<float>.Empty, 5, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        rig.ChunkClient.SearchCallCount.Should().Be(0);
    }

    // ---- SearchChunksAsync: the results ------------------------------------

    [Fact]
    public async Task SearchChunks_MapsEveryFieldOfAHitOntoTheVendorNeutralResult()
    {
        using Rig rig = CreateRig();

        Guid chunkId = Guid.CreateVersion7();
        Guid documentId = Guid.CreateVersion7();
        rig.ChunkClient.SearchHits.Add(Hit(
            chunkId, documentId, chunkOrder: 7, text: "the refund policy",
            blobUri: "https://acct.blob.core.windows.net/documents/2026/08/01/policy.pdf", score: 0.8123));

        Result<IReadOnlyList<ChunkSearchResult>> result =
            await rig.Service.SearchChunksAsync(QueryVector, topK: 5, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle().Which.Should().Be(new ChunkSearchResult(
            ChunkId: chunkId,
            DocumentId: documentId,
            ChunkOrder: 7,
            Text: "the refund policy",
            BlobUri: new Uri("https://acct.blob.core.windows.net/documents/2026/08/01/policy.pdf"),
            Score: 0.8123));
    }

    [Fact]
    public async Task SearchChunks_PreservesTheServicesRankOrder()
    {
        using Rig rig = CreateRig();

        rig.ChunkClient.SearchHits.Add(Hit(chunkOrder: 3, score: 0.9));
        rig.ChunkClient.SearchHits.Add(Hit(chunkOrder: 1, score: 0.7));
        rig.ChunkClient.SearchHits.Add(Hit(chunkOrder: 2, score: 0.5));

        Result<IReadOnlyList<ChunkSearchResult>> result =
            await rig.Service.SearchChunksAsync(QueryVector, topK: 5, CancellationToken.None);

        result.Value.Select(chunk => chunk.ChunkOrder).Should().Equal(3, 1, 2);
    }

    [Fact]
    public async Task SearchChunks_WhenAHitHasNoScore_ReportsZero()
    {
        using Rig rig = CreateRig();
        rig.ChunkClient.SearchHits.Add(Hit(score: null));

        Result<IReadOnlyList<ChunkSearchResult>> result =
            await rig.Service.SearchChunksAsync(QueryVector, topK: 5, CancellationToken.None);

        result.Value.Should().ContainSingle().Which.Score.Should().Be(0);
    }

    [Fact]
    public async Task SearchChunks_WithNoMatches_SucceedsWithAnEmptyList()
    {
        // An empty corpus, or nothing relevant in it, is a fact rather than a fault.
        using Rig rig = CreateRig();

        Result<IReadOnlyList<ChunkSearchResult>> result =
            await rig.Service.SearchChunksAsync(QueryVector, topK: 5, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Theory]
    [InlineData("chunk id", "not-a-guid", null, "https://acct.blob.core.windows.net/documents/doc.pdf")]
    [InlineData("document id", null, "not-a-guid", "https://acct.blob.core.windows.net/documents/doc.pdf")]
    [InlineData("relative blob URI", null, null, "documents/doc.pdf")]
    [InlineData("empty blob URI", null, null, "")]
    public async Task SearchChunks_SkipsHitsThatCannotBeCitedAndKeepsTheRest(
        string what,
        string? rawChunkId,
        string? rawDocumentId,
        string blobUri)
    {
        // A record nothing could resolve back to a document is dropped rather than
        // surfaced as a citation pointing nowhere. The valid hits either side keep
        // their order.
        using Rig rig = CreateRig();

        rig.ChunkClient.SearchHits.Add(Hit(chunkOrder: 0));
        rig.ChunkClient.SearchHits.Add(Hit(chunkOrder: 1, rawChunkId: rawChunkId, rawDocumentId: rawDocumentId, blobUri: blobUri));
        rig.ChunkClient.SearchHits.Add(Hit(chunkOrder: 2));

        Result<IReadOnlyList<ChunkSearchResult>> result =
            await rig.Service.SearchChunksAsync(QueryVector, topK: 5, CancellationToken.None);

        result.IsSuccess.Should().BeTrue($"a malformed {what} is skipped, not treated as a failed search");
        result.Value.Select(chunk => chunk.ChunkOrder).Should().Equal(0, 2);
    }

    // ---- SearchChunksAsync: failures ---------------------------------------

    [Fact]
    public async Task SearchChunks_WhenTheChunkIndexDoesNotExist_ReportsChunkIndexUnavailable()
    {
        // Distinct from an outage: the remedy is to ingest a document.
        using Rig rig = CreateRig();
        rig.ChunkClient.ThrowNext = new RequestFailedException(404, "index not found");

        Result<IReadOnlyList<ChunkSearchResult>> result =
            await rig.Service.SearchChunksAsync(QueryVector, topK: 5, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SearchErrors.ChunkIndexUnavailable);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(503)]
    public async Task SearchChunks_WhenTheServiceRejectsTheQuery_ReportsSearchFailed(int status)
    {
        // A 403 from a missing role assignment arrives as RequestFailedException, not
        // as a credential failure, so it is reported with the other rejections.
        using Rig rig = CreateRig();
        rig.ChunkClient.ThrowNext = new RequestFailedException(status, "rejected");

        Result<IReadOnlyList<ChunkSearchResult>> result =
            await rig.Service.SearchChunksAsync(QueryVector, topK: 5, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SearchErrors.SearchFailed);
    }

    [Fact]
    public async Task SearchChunks_WhenNoCredentialCanBeObtained_ReportsAuthenticationFailed()
    {
        using Rig rig = CreateRig();
        rig.ChunkClient.ThrowNext = new AuthenticationFailedException("no credential in the chain succeeded");

        Result<IReadOnlyList<ChunkSearchResult>> result =
            await rig.Service.SearchChunksAsync(QueryVector, topK: 5, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SearchErrors.AuthenticationFailed);
    }

    [Fact]
    public async Task SearchChunks_WhenRetriesAreExhaustedAtTheTransportLevel_ReportsSearchFailed()
    {
        // The SDK's retry policy throws AggregateException, not RequestFailedException,
        // once every attempt failed on DNS, TLS, or a socket. It must not escape.
        using Rig rig = CreateRig();
        rig.ChunkClient.ThrowNext = new AggregateException(new HttpRequestException("connection refused"));

        Result<IReadOnlyList<ChunkSearchResult>> result =
            await rig.Service.SearchChunksAsync(QueryVector, topK: 5, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SearchErrors.SearchFailed);
    }

    // ---- ListDocumentsAsync: the corpus listing ----------------------------

    private static SearchResult<IndexedDocument> DocumentHit(
        Guid? documentId = null,
        string fileName = "handbook.pdf",
        string blobName = "2026/08/01/handbook.pdf",
        string blobUri = "https://acct.blob.core.windows.net/documents/2026/08/01/handbook.pdf",
        string? rawDocumentId = null,
        DateTimeOffset? uploadedAt = null) =>
        SearchModelFactory.SearchResult(
            new IndexedDocument
            {
                DocumentId = rawDocumentId ?? (documentId ?? Guid.CreateVersion7()).ToString(),
                OriginalFileName = fileName,
                BlobName = blobName,
                BlobUri = blobUri,
                UploadedAt = uploadedAt ?? new DateTimeOffset(2026, 8, 1, 12, 30, 0, TimeSpan.Zero),
            },
            score: null,
            highlights: null);

    [Fact]
    public async Task ListDocuments_QueriesTheDocumentIndexNewestFirst()
    {
        using Rig rig = CreateRig();

        await rig.Service.ListDocumentsAsync(maxResults: 25, CancellationToken.None);

        rig.ChunkClient.SearchCallCount.Should().Be(0, "a corpus listing has no business in the chunk index");
        rig.DocumentClient.SearchCallCount.Should().Be(1);
        rig.DocumentClient.LastSearchText.Should().Be("*", "a listing matches everything rather than scoring a query");

        SearchOptions options = rig.DocumentClient.LastSearchOptions.Should().NotBeNull().And.Subject
            .Should().BeOfType<SearchOptions>().Subject;

        options.Size.Should().Be(25);
        options.OrderBy.Should().Equal("UploadedAt desc");
        options.Select.Should().Equal("DocumentId", "OriginalFileName", "BlobName", "BlobUri", "UploadedAt");
        options.VectorSearch.Should().BeNull("this is a listing, not a vector query");
    }

    [Fact]
    public async Task ListDocuments_MapsEveryFieldOntoThePortsReadModel()
    {
        using Rig rig = CreateRig();
        var documentId = Guid.CreateVersion7();
        var uploadedAt = new DateTimeOffset(2026, 9, 19, 8, 15, 0, TimeSpan.Zero);
        rig.DocumentClient.SearchHits.Add(DocumentHit(
            documentId,
            fileName: "rapor-şubat.pdf",
            blobName: "2026/09/19/rapor.pdf",
            blobUri: "https://acct.blob.core.windows.net/documents/2026/09/19/rapor.pdf",
            uploadedAt: uploadedAt));

        Result<IReadOnlyList<DocumentIndexEntry>> result =
            await rig.Service.ListDocumentsAsync(maxResults: 25, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        DocumentIndexEntry entry = result.Value.Should().ContainSingle().Subject;
        entry.DocumentId.Should().Be(documentId);
        entry.OriginalFileName.Should().Be("rapor-şubat.pdf");
        entry.BlobName.Should().Be("2026/09/19/rapor.pdf");
        entry.BlobUri.Should().Be(new Uri("https://acct.blob.core.windows.net/documents/2026/09/19/rapor.pdf"));
        entry.UploadedAt.Should().Be(uploadedAt);
    }

    [Theory]
    [InlineData("not-a-guid", "https://acct.blob.core.windows.net/documents/doc.pdf", "key")]
    [InlineData(null, "not-a-uri", "blob URI")]
    public async Task ListDocuments_SkipsARecordItCannotIdentify(string? rawId, string blobUri, string what)
    {
        // Same rule as retrieval: a row that resolves to nothing is worse on a
        // screen than one row fewer, and the whole listing is still useful.
        using Rig rig = CreateRig();
        rig.DocumentClient.SearchHits.Add(DocumentHit(fileName: "first.pdf"));
        rig.DocumentClient.SearchHits.Add(DocumentHit(rawDocumentId: rawId, blobUri: blobUri, fileName: "broken.pdf"));
        rig.DocumentClient.SearchHits.Add(DocumentHit(fileName: "third.pdf"));

        Result<IReadOnlyList<DocumentIndexEntry>> result =
            await rig.Service.ListDocumentsAsync(maxResults: 25, CancellationToken.None);

        result.IsSuccess.Should().BeTrue($"a malformed {what} is skipped, not treated as a failed listing");
        result.Value.Select(entry => entry.OriginalFileName).Should().Equal("first.pdf", "third.pdf");
    }

    [Fact]
    public async Task ListDocuments_WhenTheDocumentIndexDoesNotExist_ReportsAnEmptyCorpus()
    {
        // The index is created by the first ingestion, so 404 means nothing has
        // been uploaded — an empty list, not a failure the caller must decode.
        using Rig rig = CreateRig();
        rig.DocumentClient.ThrowNext = new RequestFailedException(404, "index not found");

        Result<IReadOnlyList<DocumentIndexEntry>> result =
            await rig.Service.ListDocumentsAsync(maxResults: 25, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Theory]
    [InlineData(400)]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(503)]
    public async Task ListDocuments_WhenTheServiceRejectsTheQuery_ReportsSearchFailed(int status)
    {
        using Rig rig = CreateRig();
        rig.DocumentClient.ThrowNext = new RequestFailedException(status, "rejected");

        Result<IReadOnlyList<DocumentIndexEntry>> result =
            await rig.Service.ListDocumentsAsync(maxResults: 25, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SearchErrors.SearchFailed);
    }

    [Fact]
    public async Task ListDocuments_WhenNoCredentialCanBeObtained_ReportsAuthenticationFailed()
    {
        using Rig rig = CreateRig();
        rig.DocumentClient.ThrowNext = new AuthenticationFailedException("no credential in the chain succeeded");

        Result<IReadOnlyList<DocumentIndexEntry>> result =
            await rig.Service.ListDocumentsAsync(maxResults: 25, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SearchErrors.AuthenticationFailed);
    }

    [Fact]
    public async Task ListDocuments_WhenRetriesAreExhaustedAtTheTransportLevel_ReportsSearchFailed()
    {
        using Rig rig = CreateRig();
        rig.DocumentClient.ThrowNext = new AggregateException(new HttpRequestException("connection refused"));

        Result<IReadOnlyList<DocumentIndexEntry>> result =
            await rig.Service.ListDocumentsAsync(maxResults: 25, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SearchErrors.SearchFailed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ListDocuments_RejectsANonPositiveLimitBeforeCallingTheService(int maxResults)
    {
        // A programming error, not a caller's mistake: the query's validator
        // bounds what reaches here, so this guards the port's own contract.
        using Rig rig = CreateRig();

        Func<Task> act = () => rig.Service.ListDocumentsAsync(maxResults, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        rig.DocumentClient.SearchCallCount.Should().Be(0);
    }

    // ---- IndexDocumentAsync: the write -------------------------------------

    [Fact]
    public async Task IndexDocument_WritesOneRecordWithEveryFieldToTheDocumentIndex()
    {
        using Rig rig = CreateRig(indexExists: true);
        DocumentIndexRequest request = DocumentRequest();

        Result result = await rig.Service.IndexDocumentAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        rig.ChunkClient.BatchSizes.Should().BeEmpty("document metadata never goes to the chunk index");
        rig.DocumentClient.BatchSizes.Should().Equal(1);

        IndexedDocument document = rig.DocumentClient.AllDocuments.Should().ContainSingle()
            .Which.Should().BeOfType<IndexedDocument>().Subject;

        document.DocumentId.Should().Be(request.DocumentId.ToString());
        document.BlobName.Should().Be(request.BlobName);
        document.OriginalFileName.Should().Be("rapor-şubat.pdf", "the index stores the name as supplied");
        document.BlobUri.Should().Be(request.BlobUri.AbsoluteUri);
        document.UploadedAt.Should().Be(request.UploadedAt);
    }

    [Fact]
    public async Task IndexDocument_WhenTheDocumentIsRejectedInsideASuccessfulResponse_ReportsDocumentRejected()
    {
        // HTTP 200 with a per-document failure: "no exception" is not "indexed".
        using Rig rig = CreateRig(indexExists: true);
        rig.DocumentClient.FailuresInNextBatch = 1;

        Result result = await rig.Service.IndexDocumentAsync(DocumentRequest(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SearchErrors.DocumentRejected);
    }

    [Fact]
    public async Task IndexDocument_WhenTheResponseCarriesNoResultForTheDocument_ReportsDocumentRejected()
    {
        using Rig rig = CreateRig(indexExists: true);
        rig.DocumentClient.TruncateResultsTo = 0;

        Result result = await rig.Service.IndexDocumentAsync(DocumentRequest(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SearchErrors.DocumentRejected);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(403)]
    [InlineData(503)]
    public async Task IndexDocument_WhenTheServiceRejectsTheWrite_ReportsIndexingFailed(int status)
    {
        using Rig rig = CreateRig(indexExists: true);
        rig.DocumentClient.ThrowNext = new RequestFailedException(status, "rejected");

        Result result = await rig.Service.IndexDocumentAsync(DocumentRequest(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SearchErrors.IndexingFailed);
    }

    [Fact]
    public async Task IndexDocument_WhenNoCredentialCanBeObtainedForTheWrite_ReportsAuthenticationFailed()
    {
        using Rig rig = CreateRig(indexExists: true);
        rig.DocumentClient.ThrowNext = new AuthenticationFailedException("no credential in the chain succeeded");

        Result result = await rig.Service.IndexDocumentAsync(DocumentRequest(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SearchErrors.AuthenticationFailed);
    }

    [Fact]
    public async Task IndexDocument_WhenTheWriteFailsAtTheTransportLevel_ReportsIndexingFailed()
    {
        using Rig rig = CreateRig(indexExists: true);
        rig.DocumentClient.ThrowNext = new AggregateException(new HttpRequestException("connection refused"));

        Result result = await rig.Service.IndexDocumentAsync(DocumentRequest(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SearchErrors.IndexingFailed);
    }

    // ---- IndexDocumentAsync: provisioning the document index ---------------

    [Fact]
    public async Task IndexDocument_CreatesTheDocumentIndexFromItsSchemaOnFirstUse()
    {
        using Rig rig = CreateRig(indexExists: false);

        Result result = await rig.Service.IndexDocumentAsync(DocumentRequest(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        rig.IndexClient.CreateCallCount.Should().Be(1);
        rig.IndexClient.CreatedIndex!.Name.Should().Be(DocumentIndex,
            "this adapter owns the document index; the chunk index belongs to the vector adapter");
        rig.IndexClient.CreatedIndex.Fields.Select(field => field.Name)
            .Should().BeEquivalentTo(DocumentIndexSchema.Build(DocumentIndex).Fields.Select(field => field.Name));
        rig.DocumentClient.BatchSizes.Should().ContainSingle("the document is written once the index exists")
            .Which.Should().Be(1);
    }

    [Fact]
    public async Task IndexDocument_WhenTheIndexAlreadyExists_DoesNotRecreateIt()
    {
        using Rig rig = CreateRig(indexExists: true);

        await rig.Service.IndexDocumentAsync(DocumentRequest(), CancellationToken.None);

        rig.IndexClient.GetCallCount.Should().Be(1);
        rig.IndexClient.CreateCallCount.Should().Be(0, "Create, never CreateOrUpdate: an existing schema is left alone");
    }

    [Fact]
    public async Task IndexDocument_ChecksForTheIndexOnlyOnceAcrossCalls()
    {
        using Rig rig = CreateRig(indexExists: false);

        await rig.Service.IndexDocumentAsync(DocumentRequest(), CancellationToken.None);
        await rig.Service.IndexDocumentAsync(DocumentRequest(), CancellationToken.None);

        rig.IndexClient.GetCallCount.Should().Be(1);
        rig.IndexClient.CreateCallCount.Should().Be(1);
        rig.DocumentClient.BatchSizes.Should().Equal(1, 1);
    }

    [Fact]
    public async Task IndexDocument_WhenAnotherInstanceCreatesTheIndexFirst_TreatsTheConflictAsSuccess()
    {
        // 404 on get, then 409 on create: someone else won the race, which is the
        // outcome that was wanted.
        using Rig rig = CreateRig(indexExists: false);
        rig.IndexClient.ThrowNextCreate = new RequestFailedException(409, "index already exists");

        Result first = await rig.Service.IndexDocumentAsync(DocumentRequest(), CancellationToken.None);
        Result second = await rig.Service.IndexDocumentAsync(DocumentRequest(), CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        rig.DocumentClient.BatchSizes.Should().Equal(1, 1);
        rig.IndexClient.GetCallCount.Should().Be(1, "a conflict still counts as verified, so it is not re-checked");
    }

    [Theory]
    [InlineData("get", 403)]
    [InlineData("get", 503)]
    [InlineData("create", 400)]
    [InlineData("create", 403)]
    public async Task IndexDocument_WhenTheIndexCannotBeConfirmedOrCreated_ReportsIndexUnavailableAndWritesNothing(
        string operation,
        int status)
    {
        using Rig rig = CreateRig(indexExists: false);
        var failure = new RequestFailedException(status, "rejected");

        if (operation == "get")
        {
            rig.IndexClient.ThrowNextGet = failure;
        }
        else
        {
            rig.IndexClient.ThrowNextCreate = failure;
        }

        Result result = await rig.Service.IndexDocumentAsync(DocumentRequest(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SearchErrors.IndexUnavailable);
        rig.DocumentClient.BatchSizes.Should().BeEmpty();
    }

    [Fact]
    public async Task IndexDocument_WhenNoCredentialCanBeObtainedForProvisioning_ReportsAuthenticationFailed()
    {
        using Rig rig = CreateRig(indexExists: false);
        rig.IndexClient.ThrowNextGet = new AuthenticationFailedException("no credential in the chain succeeded");

        Result result = await rig.Service.IndexDocumentAsync(DocumentRequest(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SearchErrors.AuthenticationFailed);
        rig.DocumentClient.BatchSizes.Should().BeEmpty();
    }

    [Fact]
    public async Task IndexDocument_WhenProvisioningFailsAtTheTransportLevel_ReportsIndexUnavailable()
    {
        using Rig rig = CreateRig(indexExists: false);
        rig.IndexClient.ThrowNextGet = new AggregateException(new HttpRequestException("connection refused"));

        Result result = await rig.Service.IndexDocumentAsync(DocumentRequest(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SearchErrors.IndexUnavailable);
    }

    [Fact]
    public async Task IndexDocument_AfterAFailedIndexCheck_TriesAgainOnTheNextCall()
    {
        // Not a cached faulted task: one transient failure must not poison every
        // later upload for the life of the process.
        using Rig rig = CreateRig(indexExists: true);
        rig.IndexClient.ThrowNextGet = new RequestFailedException(503, "unavailable");

        Result first = await rig.Service.IndexDocumentAsync(DocumentRequest(), CancellationToken.None);
        Result second = await rig.Service.IndexDocumentAsync(DocumentRequest(), CancellationToken.None);

        first.IsFailure.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        rig.IndexClient.GetCallCount.Should().Be(2);
        rig.DocumentClient.BatchSizes.Should().Equal(1);
    }
}
