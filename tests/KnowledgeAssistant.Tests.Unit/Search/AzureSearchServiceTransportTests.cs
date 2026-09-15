using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using KnowledgeAssistant.Infrastructure.Search;
using KnowledgeAssistant.Infrastructure.Search.Vectors;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KnowledgeAssistant.Tests.Unit.Search;

/// <summary>
/// The HTTP request a retrieval query actually becomes.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AzureSearchServiceTests"/> proves the adapter builds the right
/// <see cref="SearchOptions"/>. This proves what the unmodified
/// <c>Azure.Search.Documents</c> pipeline turns those options into on the wire:
/// the method, the path, the API version, the credential header, and the vector
/// query body. A field-name casing change or a serializer disagreement would pass
/// the options test and fail here.
/// </para>
/// <para>
/// <b>Only the socket is replaced.</b> The client is a real
/// <see cref="SearchIndexClient"/> with a real bearer-token policy; the transport is
/// Azure.Core's own <see cref="HttpClientTransport"/> over a handler that records the
/// request and answers with an empty result page. Nothing leaves the process.
/// </para>
/// </remarks>
public sealed class AzureSearchServiceTransportTests
{
    [Fact]
    public async Task SearchChunks_SendsAnEntraAuthenticatedVectorQueryToTheChunkIndexDocsSearchEndpoint()
    {
        var handler = new RecordingHandler("""{"value":[]}""");

        var clientOptions = new SearchClientOptions
        {
            Transport = new HttpClientTransport(handler),
        };

        // A single attempt, so the recorded request is the only request.
        clientOptions.Retry.MaxRetries = 0;

        var indexClient = new SearchIndexClient(
            new Uri("https://fake.search.windows.net/"),
            new FixedTokenCredential(),
            clientOptions);

        using var service = new AzureSearchService(
            indexClient,
            Options.Create(new AzureSearchOptions
            {
                Endpoint = "https://fake.search.windows.net/",
                IndexName = "knowledge-index",
                ChunkIndexName = "knowledge-chunks",
            }),
            NullLogger<AzureSearchService>.Instance);

        var result = await service.SearchChunksAsync(new[] { 0.5f, -0.25f, 1f }, topK: 5, CancellationToken.None);

        result.IsSuccess.Should().BeTrue("an empty page is a successful search with no matches");
        handler.Requests.Should().ContainSingle();

        RecordedRequest request = handler.Requests[0];

        // Method, path, and API version.
        request.Method.Should().Be(HttpMethod.Post);
        request.Uri.Host.Should().Be("fake.search.windows.net");
        Uri.UnescapeDataString(request.Uri.AbsolutePath).Should().Be("/indexes('knowledge-chunks')/docs/search.post.search");
        request.Uri.Query.Should().Contain("api-version=2026-04-01");

        // Entra ID, not a key: the production credential design, end to end.
        request.Authorization.Should().Be("Bearer fixed-test-token");
        request.HasApiKeyHeader.Should().BeFalse("this service authenticates with a token and never sends an api-key");

        // The body.
        using JsonDocument body = JsonDocument.Parse(request.Body);
        JsonElement root = body.RootElement;

        bool hasSearchText = root.TryGetProperty("search", out JsonElement search)
            && search.ValueKind != JsonValueKind.Null;

        hasSearchText.Should().BeFalse("a pure vector query carries no search text");
        root.GetProperty("top").GetInt32().Should().Be(5);
        root.GetProperty("select").GetString().Should().Be("ChunkId,DocumentId,ChunkOrder,ChunkText,BlobUri");

        JsonElement vectorQuery = root.GetProperty("vectorQueries").EnumerateArray().Should().ContainSingle().Subject;

        vectorQuery.GetProperty("kind").GetString().Should().Be("vector");
        vectorQuery.GetProperty("k").GetInt32().Should().Be(5);
        vectorQuery.GetProperty("fields").GetString().Should().Be(nameof(ChunkSearchDocument.Embedding));
        vectorQuery.GetProperty("vector").EnumerateArray().Select(value => value.GetSingle())
            .Should().Equal(0.5f, -0.25f, 1f);
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri Uri,
        string? Authorization,
        bool HasApiKeyHeader,
        string Body);

    /// <summary>Records each request and answers with a fixed JSON body.</summary>
    private sealed class RecordingHandler(string responseJson) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization?.ToString(),
                request.Headers.Contains("api-key"),
                body));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            };
        }
    }

    /// <summary>A token credential that never contacts Entra ID.</summary>
    private sealed class FixedTokenCredential : TokenCredential
    {
        private static readonly AccessToken Token =
            new("fixed-test-token", new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero));

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            Token;

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Token);
    }
}
