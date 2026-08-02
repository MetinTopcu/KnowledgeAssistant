using System.Text.Json;
using KnowledgeAssistant.Application.Interfaces;
using KnowledgeAssistant.Domain.Common;
using KnowledgeAssistant.Infrastructure.Azure.Agents;
using KnowledgeAssistant.Tests.Unit.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeAssistant.Tests.Unit.Agents;

/// <summary>
/// The one capability the agent is given, and the seam where it meets the
/// existing retrieval pipeline.
/// </summary>
/// <remarks>
/// <para>
/// <b>The first test is the important one.</b> "The agent's knowledge source is
/// the existing RAG pipeline" is the requirement this whole slice exists to
/// satisfy, and it is satisfied by the tool calling
/// <see cref="IEmbeddingService"/> and then <see cref="IAzureSearchService"/> —
/// the same two ports, in the same order, that the retrieval slice uses. Asserting
/// the stage sequence is what turns that from a claim in a comment into something
/// the build checks.
/// </para>
/// <para>
/// The schema tests look pedantic and are not. It is a hand-written string literal
/// that a model reads to decide how to call the function, and a typo in it does
/// not fail a build, fail a request, or appear in a log — it produces an agent
/// that silently stops searching.
/// </para>
/// </remarks>
public sealed class KnowledgeSearchToolTests
{
    private static KnowledgeSearchTool CreateTool(
        PipelineRig rig,
        int maxSearchResultCharacters = 24_000) =>
        new(
            rig.Embedding,
            rig.Search,
            new FoundryAgentOptions { MaxSearchResultCharacters = maxSearchResultCharacters },
            NullLogger<KnowledgeSearchTool>.Instance);

    [Fact]
    public async Task SearchAsync_EmbedsTheQueryThenSearchesWithThatVector()
    {
        // The requirement, asserted structurally: the agent retrieves through the
        // same embedding service that embedded the corpus, so its query vectors are
        // comparable to the indexed ones. A second embedding path would return
        // plausible nonsense rather than an error, which is why this is a test and
        // not a comment.
        var rig = new PipelineRig();

        Result<IReadOnlyList<ChunkSearchResult>> result =
            await CreateTool(rig).SearchAsync("refund policy", maxResults: 4, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        rig.Trace.Stages.Should().Equal("EmbedText", "Search");
        rig.Embedding.ReceivedText.Should().Be("refund policy");
        rig.Search.ReceivedVector.Span.SequenceEqual(rig.Embedding.QueryVector.Span).Should().BeTrue();
        rig.Search.ReceivedTopK.Should().Be(4, "the caller's ceiling bounds the search, not the model's opinion");
    }

    [Fact]
    public async Task SearchAsync_WhenEmbeddingFails_PassesTheErrorThroughWithoutSearching()
    {
        var rig = new PipelineRig();
        rig.Embedding.TextError = Error.Failure("Embedding.RateLimited", "nope");

        Result<IReadOnlyList<ChunkSearchResult>> result =
            await CreateTool(rig).SearchAsync("anything", maxResults: 4, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Embedding.RateLimited",
            "the caller must learn which stage broke, not merely that the agent did");
        rig.Trace.Stages.Should().Equal("EmbedText");
    }

    [Fact]
    public async Task SearchAsync_WhenSearchFails_PassesTheErrorThrough()
    {
        var rig = new PipelineRig();
        rig.Search.SearchError = Error.Failure("Search.QueryFailed", "nope");

        Result<IReadOnlyList<ChunkSearchResult>> result =
            await CreateTool(rig).SearchAsync("anything", maxResults: 4, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Search.QueryFailed");
    }

    [Fact]
    public async Task SearchAsync_WithNoMatches_SucceedsWithAnEmptyList()
    {
        // An empty corpus is a fact the agent should reason about — its instructions
        // tell it to try different wording — not a failure that ends the run.
        var rig = new PipelineRig();
        rig.Search.ResultCount = 0;

        Result<IReadOnlyList<ChunkSearchResult>> result =
            await CreateTool(rig).SearchAsync("anything", maxResults: 4, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_PassesTheCallersCancellationTokenToBothPorts()
    {
        var rig = new PipelineRig();
        using var cancellation = new CancellationTokenSource();

        await CreateTool(rig).SearchAsync("anything", maxResults: 4, cancellation.Token);

        rig.Trace.Tokens.Should().AllSatisfy(token => token.Should().Be(cancellation.Token));
    }

    [Theory]
    [InlineData("""{"query":"refund policy"}""", "refund policy")]
    [InlineData("""{"query":"refund policy","extra":1}""", "refund policy")]
    public void TryReadQuery_ReadsTheQuery(string arguments, string expected) =>
        KnowledgeSearchTool.TryReadQuery(BinaryData.FromString(arguments)).Should().Be(expected);

    [Theory]
    [InlineData("""{"query":""}""")]
    [InlineData("""{"query":"   "}""")]
    [InlineData("""{"query":42}""")]
    [InlineData("""{"other":"x"}""")]
    [InlineData("""[]""")]
    [InlineData("""not json at all""")]
    [InlineData("")]
    public void TryReadQuery_WithUnusableArguments_ReturnsNull(string arguments) =>
        // Strict mode makes these close to impossible, which is exactly why they
        // are handled by returning null rather than throwing: an unusable call is
        // worth reporting back to an agent that can try again, not worth failing a
        // request that is otherwise proceeding.
        KnowledgeSearchTool.TryReadQuery(BinaryData.FromString(arguments)).Should().BeNull();

    [Fact]
    public void TryReadQuery_WithNoArguments_ReturnsNull() =>
        KnowledgeSearchTool.TryReadQuery(null).Should().BeNull();

    [Fact]
    public void RenderResult_EmitsTheReferenceNumberScoreAndTextItWasGiven()
    {
        var rig = new PipelineRig();
        var tool = CreateTool(rig);

        string json = tool.RenderResult(
        [
            (3, Source("alpha", 0.91)),
            (4, Source("beta", 0.42)),
        ]);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement sources = document.RootElement.GetProperty("sources");

        sources.GetArrayLength().Should().Be(2);
        sources[0].GetProperty("ref").GetInt32().Should().Be(3,
            "reference numbers are assigned across the whole run, not per search");
        sources[0].GetProperty("text").GetString().Should().Be("alpha");
        sources[0].GetProperty("score").GetDouble().Should().BeApproximately(0.91, 0.0001);
        sources[1].GetProperty("ref").GetInt32().Should().Be(4);
    }

    [Fact]
    public void RenderResult_OmitsIdentifiersTheModelCannotUse()
    {
        // Chunk ids, document ids, and blob URIs are billed as prompt tokens on
        // every subsequent turn and buy the model nothing: the reference number
        // already joins each passage back to the full record the response returns.
        var rig = new PipelineRig();

        string json = CreateTool(rig).RenderResult([(1, Source("alpha", 0.9))]);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement first = document.RootElement.GetProperty("sources")[0];

        first.TryGetProperty("chunk_id", out _).Should().BeFalse();
        first.TryGetProperty("document_id", out _).Should().BeFalse();
        first.TryGetProperty("blob_uri", out _).Should().BeFalse();
    }

    [Fact]
    public void RenderResult_StopsAtTheCharacterBudgetAndKeepsTheMostRelevant()
    {
        // Passages arrive in rank order, so trimming from the end drops the least
        // relevant rather than an arbitrary slice.
        var rig = new PipelineRig();
        var tool = CreateTool(rig, maxSearchResultCharacters: 1_000);

        string json = tool.RenderResult(
        [
            (1, Source(new string('a', 600), 0.9)),
            (2, Source(new string('b', 600), 0.5)),
        ]);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement sources = document.RootElement.GetProperty("sources");

        sources.GetArrayLength().Should().Be(1, "the second passage would exceed the budget");
        sources[0].GetProperty("ref").GetInt32().Should().Be(1);
    }

    [Fact]
    public void RenderResult_WithNothingToRender_EmitsAnEmptySourceList()
    {
        var rig = new PipelineRig();

        using JsonDocument document = JsonDocument.Parse(CreateTool(rig).RenderResult([]));

        document.RootElement.GetProperty("sources").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void ParameterSchema_DeclaresExactlyTheQueryArgumentAndRequiresIt()
    {
        // A hand-written literal that a model reads to decide how to call the
        // function. A typo in it fails no build and no request; it produces an
        // agent that silently stops searching.
        using JsonDocument document = JsonDocument.Parse(KnowledgeSearchTool.ParameterSchema);
        JsonElement root = document.RootElement;

        root.GetProperty("type").GetString().Should().Be("object");
        // Exactly one property: the model chooses the query and never the budget.
        root.GetProperty("properties").EnumerateObject().Select(property => property.Name)
            .Should().ContainSingle().Which.Should().Be("query");
        root.GetProperty("properties").GetProperty("query").GetProperty("type").GetString().Should().Be("string");

        // Both are what strict mode demands, and strict mode is what makes the
        // arguments structurally guaranteed rather than merely likely.
        root.GetProperty("required").EnumerateArray().Select(item => item.GetString()).Should().Equal("query");
        root.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
    }

    [Theory]
    [InlineData(nameof(KnowledgeSearchTool.RenderUnusableCall))]
    [InlineData(nameof(KnowledgeSearchTool.RenderBudgetExhausted))]
    [InlineData(nameof(KnowledgeSearchTool.RenderUnknownFunction))]
    public void EveryFallbackOutput_IsValidJsonShapedLikeAnEmptyResult(string which)
    {
        // The protocol requires every tool call to be answered, so these three are
        // what the adapter sends when it cannot answer one normally. Each must parse
        // and must look like an ordinary empty result, because the agent's
        // instructions already tell it how to handle finding nothing.
        string json = which switch
        {
            nameof(KnowledgeSearchTool.RenderUnusableCall) => KnowledgeSearchTool.RenderUnusableCall(),
            nameof(KnowledgeSearchTool.RenderBudgetExhausted) => KnowledgeSearchTool.RenderBudgetExhausted(),
            _ => KnowledgeSearchTool.RenderUnknownFunction(),
        };

        using JsonDocument document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("sources").GetArrayLength().Should().Be(0);
        document.RootElement.GetProperty("note").GetString().Should().NotBeNullOrWhiteSpace();
    }

    private static ChunkSearchResult Source(string text, double score) => new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        0,
        text,
        new Uri("https://acct.blob.core.windows.net/documents/doc.pdf"),
        score);
}
