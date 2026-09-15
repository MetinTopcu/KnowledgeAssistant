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
/// A <see cref="SearchClient"/> that records the batches and queries it was sent
/// and can be scripted to fail.
/// </summary>
/// <remarks>
/// <para>
/// Reports per-document outcomes inside a successful response, which is the
/// condition the indexing adapters exist to catch: Azure AI Search answers 200 for
/// a batch in which individual documents were rejected.
/// </para>
/// <para>
/// Search responses are built with the SDK's own <see cref="SearchModelFactory"/>,
/// so the adapter enumerates a genuine <see cref="SearchResults{T}"/> rather than
/// a stand-in shaped like one.
/// </para>
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

    public int SearchCallCount { get; private set; }

    /// <summary>The search text of the most recent query; null for a pure vector query.</summary>
    public string? LastSearchText { get; private set; }

    /// <summary>The options of the most recent query, exactly as the adapter built them.</summary>
    public SearchOptions? LastSearchOptions { get; private set; }

    /// <summary>The cancellation token the most recent query was issued with.</summary>
    public CancellationToken LastSearchToken { get; private set; }

    /// <summary>
    /// The hits the next query returns, as <c>SearchResult&lt;T&gt;</c> for the
    /// document type the adapter searches with.
    /// </summary>
    public List<object> SearchHits { get; } = [];

    public override Task<Response<SearchResults<T>>> SearchAsync<T>(
        string? searchText,
        SearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        SearchCallCount++;
        LastSearchText = searchText;
        LastSearchOptions = options;
        LastSearchToken = cancellationToken;

        if (ThrowNext is not null)
        {
            Exception exception = ThrowNext;
            ThrowNext = null;
            throw exception;
        }

        SearchResults<T> results = SearchModelFactory.SearchResults(
            SearchHits.Cast<SearchResult<T>>(),
            totalCount: null,
            facets: null,
            coverage: null,
            rawResponse: new FakeAzureResponse());

        return Task.FromResult(Response.FromValue(results, new FakeAzureResponse()));
    }

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
/// <remarks>
/// Hands out the same client for every index unless one is registered for a
/// specific name in <see cref="ClientsByIndex"/>, which is how a test tells a
/// write to the document index apart from a query against the chunk index.
/// </remarks>
internal sealed class FakeSearchIndexClient(FakeSearchClient searchClient) : SearchIndexClient
{
    public bool IndexExists { get; set; }

    public SearchIndex? CreatedIndex { get; private set; }

    public int CreateCallCount { get; private set; }

    public int GetCallCount { get; private set; }

    public int StatisticsCallCount { get; private set; }

    /// <summary>Clients for specific index names, overriding the default one.</summary>
    public Dictionary<string, FakeSearchClient> ClientsByIndex { get; } = new(StringComparer.Ordinal);

    /// <summary>Every index name a search client was requested for, in order.</summary>
    public List<string> RequestedClientNames { get; } = [];

    /// <summary>Thrown by the next <c>GetIndexAsync</c>, then cleared.</summary>
    public Exception? ThrowNextGet { get; set; }

    /// <summary>Thrown by the next <c>CreateIndexAsync</c>, then cleared.</summary>
    public Exception? ThrowNextCreate { get; set; }

    /// <summary>Thrown by the next <c>GetServiceStatisticsAsync</c>, then cleared.</summary>
    public Exception? ThrowNextStatistics { get; set; }

    public override SearchClient GetSearchClient(string indexName)
    {
        RequestedClientNames.Add(indexName);

        return ClientsByIndex.TryGetValue(indexName, out FakeSearchClient? specific) ? specific : searchClient;
    }

    public override Task<Response<SearchIndex>> GetIndexAsync(
        string indexName,
        CancellationToken cancellationToken = default)
    {
        GetCallCount++;

        ThrowIfScripted(ThrowNextGet, () => ThrowNextGet = null);

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

        ThrowIfScripted(ThrowNextCreate, () => ThrowNextCreate = null);

        CreatedIndex = index;
        IndexExists = true;

        return Task.FromResult(Response.FromValue(index, new FakeAzureResponse()));
    }

    public override Task<Response<SearchServiceStatistics>> GetServiceStatisticsAsync(
        CancellationToken cancellationToken = default)
    {
        StatisticsCallCount++;

        ThrowIfScripted(ThrowNextStatistics, () => ThrowNextStatistics = null);

        SearchServiceStatistics statistics = SearchModelFactory.SearchServiceStatistics(
            SearchModelFactory.SearchServiceCounters(null, null, null, null, null, null),
            SearchModelFactory.SearchServiceLimits(null, null, null, null));

        return Task.FromResult(Response.FromValue(statistics, new FakeAzureResponse()));
    }

    private static void ThrowIfScripted(Exception? exception, Action clear)
    {
        if (exception is not null)
        {
            clear();
            throw exception;
        }
    }
}
