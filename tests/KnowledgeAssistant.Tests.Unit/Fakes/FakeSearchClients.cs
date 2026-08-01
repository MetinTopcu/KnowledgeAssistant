using System.Diagnostics.CodeAnalysis;
using Azure;
using Azure.Core;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;

namespace KnowledgeAssistant.Tests.Unit.Fakes;

/// <summary>A minimal <see cref="Response"/> for wrapping faked values.</summary>
internal sealed class FakeAzureResponse : Response
{
    public override int Status => 200;

    public override string ReasonPhrase => "OK";

    public override Stream? ContentStream { get; set; }

    public override string ClientRequestId { get; set; } = "fake";

    public override void Dispose()
    {
    }

    protected override bool ContainsHeader(string name) => false;

    protected override IEnumerable<HttpHeader> EnumerateHeaders() => [];

    protected override bool TryGetHeader(string name, [NotNullWhen(true)] out string? value)
    {
        value = null;
        return false;
    }

    protected override bool TryGetHeaderValues(string name, [NotNullWhen(true)] out IEnumerable<string>? values)
    {
        values = null;
        return false;
    }
}

/// <summary>
/// A <see cref="SearchClient"/> that records the batches it was sent and can be
/// scripted to fail.
/// </summary>
/// <remarks>
/// Reports per-document outcomes inside a successful response, which is the
/// condition the vector adapter exists to catch: Azure AI Search answers 200 for
/// a batch in which individual documents were rejected.
/// </remarks>
internal sealed class FakeSearchClient : SearchClient
{
    public List<int> BatchSizes { get; } = [];

    public List<object> AllDocuments { get; } = [];

    /// <summary>Reports this many documents in the next batch as failed.</summary>
    public int FailuresInNextBatch { get; set; }

    /// <summary>Throws this on the next call, then clears itself.</summary>
    public Exception? ThrowNext { get; set; }

    /// <summary>Returns fewer results than documents sent, when non-negative.</summary>
    public int TruncateResultsTo { get; set; } = -1;

    public override Task<Response<IndexDocumentsResult>> MergeOrUploadDocumentsAsync<T>(
        IEnumerable<T> documents,
        IndexDocumentsOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (ThrowNext is not null)
        {
            Exception exception = ThrowNext;
            ThrowNext = null;
            throw exception;
        }

        List<object> batch = [.. documents.Cast<object>()];
        BatchSizes.Add(batch.Count);
        AllDocuments.AddRange(batch);

        int resultCount = TruncateResultsTo >= 0 ? Math.Min(TruncateResultsTo, batch.Count) : batch.Count;
        int failures = FailuresInNextBatch;
        FailuresInNextBatch = 0;

        var results = new List<IndexingResult>(resultCount);

        for (int index = 0; index < resultCount; index++)
        {
            bool succeeded = index >= failures;

            results.Add(SearchModelFactory.IndexingResult(
                key: $"key-{index}",
                errorMessage: succeeded ? null : "throttled",
                succeeded: succeeded,
                status: succeeded ? 200 : 429));
        }

        return Task.FromResult(
            Response.FromValue(SearchModelFactory.IndexDocumentsResult(results), new FakeAzureResponse()));
    }
}

/// <summary>
/// A <see cref="SearchIndexClient"/> that records index provisioning and hands
/// out a scripted <see cref="FakeSearchClient"/>.
/// </summary>
internal sealed class FakeSearchIndexClient(FakeSearchClient searchClient) : SearchIndexClient
{
    public bool IndexExists { get; set; }

    public SearchIndex? CreatedIndex { get; private set; }

    public int CreateCallCount { get; private set; }

    public int GetCallCount { get; private set; }

    public override SearchClient GetSearchClient(string indexName) => searchClient;

    public override Task<Response<SearchIndex>> GetIndexAsync(
        string indexName,
        CancellationToken cancellationToken = default)
    {
        GetCallCount++;

        if (!IndexExists)
        {
            throw new RequestFailedException(404, "index not found");
        }

        return Task.FromResult(Response.FromValue(new SearchIndex(indexName), new FakeAzureResponse()));
    }

    public override Task<Response<SearchIndex>> CreateIndexAsync(
        SearchIndex index,
        CancellationToken cancellationToken = default)
    {
        CreateCallCount++;
        CreatedIndex = index;
        IndexExists = true;

        return Task.FromResult(Response.FromValue(index, new FakeAzureResponse()));
    }
}
