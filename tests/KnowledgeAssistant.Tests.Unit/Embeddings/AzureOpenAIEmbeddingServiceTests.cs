using System.Diagnostics;
using Azure.Identity;
using KnowledgeAssistant.Application.Interfaces;
using KnowledgeAssistant.Domain.Common;
using KnowledgeAssistant.Infrastructure.Azure.OpenAI;
using KnowledgeAssistant.Tests.Unit.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KnowledgeAssistant.Tests.Unit.Embeddings;

/// <summary>
/// The embedding adapter: batching, retry, and response integrity.
/// </summary>
/// <remarks>
/// Exercised against a faked <c>EmbeddingClient</c>, so the conditions that
/// matter most — a 429 followed by a success, a <c>Retry-After</c> of an hour, a
/// response with the wrong number of vectors — are provoked directly instead of
/// waited for.
/// </remarks>
public sealed class AzureOpenAIEmbeddingServiceTests
{
    private static AzureOpenAIOptions Options(
        int batch = 16,
        int dimensions = 8,
        int retries = 4,
        double baseDelay = 0.05,
        double maxDelay = 30) => new()
        {
            Endpoint = "https://fake.services.ai.azure.com/",
            EmbeddingDeploymentName = "text-embedding-3-small",
            ChatDeploymentName = "gpt-4o-mini",
            EmbeddingDimensions = dimensions,
            EmbeddingBatchSize = batch,
            MaxRetryAttempts = retries,
            RetryBaseDelaySeconds = baseDelay,
            RetryMaxDelaySeconds = maxDelay,
        };

    private static IEmbeddingService Create(FakeEmbeddingClient client, AzureOpenAIOptions options) =>
        new AzureOpenAIEmbeddingService(
            client,
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<AzureOpenAIEmbeddingService>.Instance);

    private static IReadOnlyList<DocumentChunk> Chunks(int count) =>
        [.. Enumerable.Range(0, count).Select(i => new DocumentChunk(Guid.CreateVersion7(), i, $"chunk text {i}"))];

    [Fact]
    public async Task GenerateEmbeddings_SplitsInputIntoBatches()
    {
        var client = new FakeEmbeddingClient().AlwaysSucceed();
        IEmbeddingService service = Create(client, Options(batch: 16));

        Result<IReadOnlyList<ChunkEmbedding>> result =
            await service.GenerateEmbeddingsAsync(Chunks(40), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(40);
        client.CallInputCounts.Should().Equal(16, 16, 8);
    }

    [Fact]
    public async Task GenerateEmbeddings_WhenBatchExceedsInput_SendsOneRequest()
    {
        var client = new FakeEmbeddingClient().AlwaysSucceed();

        Result<IReadOnlyList<ChunkEmbedding>> result =
            await Create(client, Options(batch: 100)).GenerateEmbeddingsAsync(Chunks(40), CancellationToken.None);

        result.Value.Should().HaveCount(40);
        client.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GenerateEmbeddings_WithNoChunks_SendsNothing()
    {
        var client = new FakeEmbeddingClient().AlwaysSucceed();

        Result<IReadOnlyList<ChunkEmbedding>> result =
            await Create(client, Options()).GenerateEmbeddingsAsync([], CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
        client.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GenerateEmbeddings_PairsEachVectorWithItsOwnChunk()
    {
        var client = new FakeEmbeddingClient().AlwaysSucceed();
        IReadOnlyList<DocumentChunk> chunks = Chunks(40);

        Result<IReadOnlyList<ChunkEmbedding>> result =
            await Create(client, Options(batch: 16)).GenerateEmbeddingsAsync(chunks, CancellationToken.None);

        result.Value.Select(e => e.ChunkId).Should().Equal(chunks.Select(c => c.ChunkId));

        // The fake encodes each input's within-batch position in vector[0].
        result.Value.Select((embedding, index) => embedding.Vector.Span[0] == index % 16)
            .Should().OnlyContain(aligned => aligned);

        client.CallInputs[0][0].Should().Be("chunk text 0");
        client.CallInputs[2][7].Should().Be("chunk text 39");
    }

    [Fact]
    public async Task GenerateEmbedding_ForSingleText_SendsOneInputAndReturnsOneVector()
    {
        var client = new FakeEmbeddingClient().AlwaysSucceed();

        Result<ReadOnlyMemory<float>> result =
            await Create(client, Options()).GenerateEmbeddingAsync("what is the policy?", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Length.Should().Be(8);
        client.CallInputCounts.Should().Equal(1);
        client.CallInputs[0][0].Should().Be("what is the policy?");
    }

    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    [InlineData(500)]
    [InlineData(408)]
    public async Task GenerateEmbeddings_RetriesTransientStatuses(int status)
    {
        var client = new FakeEmbeddingClient().ThenThrow(status).ThenSucceed();

        Result<IReadOnlyList<ChunkEmbedding>> result =
            await Create(client, Options(batch: 100)).GenerateEmbeddingsAsync(Chunks(5), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        client.CallCount.Should().Be(2);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(404)]
    public async Task GenerateEmbeddings_DoesNotRetryDeterministicStatuses(int status)
    {
        var client = new FakeEmbeddingClient().ThenThrow(status).ThenSucceed();

        Result<IReadOnlyList<ChunkEmbedding>> result =
            await Create(client, Options(batch: 100)).GenerateEmbeddingsAsync(Chunks(5), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Embedding.GenerationFailed");
        client.CallCount.Should().Be(1, "repeating a deterministic failure only wastes the caller's time");
    }

    [Fact]
    public async Task GenerateEmbeddings_BoundsRetriesAndReportsRateLimitingDistinctly()
    {
        var client = new FakeEmbeddingClient().ThenThrow(429, times: 10);

        Result<IReadOnlyList<ChunkEmbedding>> result =
            await Create(client, Options(batch: 100, retries: 2)).GenerateEmbeddingsAsync(Chunks(5), CancellationToken.None);

        client.CallCount.Should().Be(3, "one attempt plus two retries");
        result.Error.Code.Should().Be("Embedding.RateLimited");
    }

    [Fact]
    public async Task GenerateEmbeddings_HonoursRetryAfterOverExponentialBackoff()
    {
        // Base delay of 20s: were Retry-After ignored, this would take ~20s.
        var client = new FakeEmbeddingClient().ThenThrow(429, retryAfter: "1").ThenSucceed();
        var stopwatch = Stopwatch.StartNew();

        Result<IReadOnlyList<ChunkEmbedding>> result = await Create(client, Options(batch: 100, retries: 3, baseDelay: 20, maxDelay: 30))
            .GenerateEmbeddingsAsync(Chunks(3), CancellationToken.None);

        stopwatch.Stop();

        result.IsSuccess.Should().BeTrue();
        stopwatch.Elapsed.Should().BeGreaterThan(TimeSpan.FromMilliseconds(700))
            .And.BeLessThan(TimeSpan.FromSeconds(6));
    }

    [Fact]
    public async Task GenerateEmbeddings_ClampsAHostileRetryAfter()
    {
        var client = new FakeEmbeddingClient().ThenThrow(429, retryAfter: "3600").ThenSucceed();
        var stopwatch = Stopwatch.StartNew();

        Result<IReadOnlyList<ChunkEmbedding>> result = await Create(client, Options(batch: 100, retries: 3, baseDelay: 0.05, maxDelay: 2))
            .GenerateEmbeddingsAsync(Chunks(3), CancellationToken.None);

        stopwatch.Stop();

        result.IsSuccess.Should().BeTrue();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(6));
    }

    [Fact]
    public async Task GenerateEmbeddings_WhenVectorLengthDiffersFromConfiguration_Fails()
    {
        var client = new FakeEmbeddingClient().AlwaysWrongDimensions(actual: 1536);

        Result<IReadOnlyList<ChunkEmbedding>> result = await Create(client, Options(batch: 100, dimensions: 8))
            .GenerateEmbeddingsAsync(Chunks(3), CancellationToken.None);

        result.Error.Code.Should().Be("Embedding.DimensionMismatch");
    }

    [Fact]
    public async Task GenerateEmbeddings_WhenDimensionCheckDisabled_AcceptsAnyLength()
    {
        var client = new FakeEmbeddingClient().AlwaysWrongDimensions(actual: 1536);

        Result<IReadOnlyList<ChunkEmbedding>> result = await Create(client, Options(batch: 100, dimensions: 0))
            .GenerateEmbeddingsAsync(Chunks(3), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(2)]
    [InlineData(9)]
    public async Task GenerateEmbeddings_WhenResponseCountDisagreesWithInput_Fails(int returned)
    {
        // Position is the only correspondence available, so a count mismatch is
        // the single detectable symptom of a misalignment.
        var client = new FakeEmbeddingClient().AlwaysReturnCount(returned);

        Result<IReadOnlyList<ChunkEmbedding>> result =
            await Create(client, Options(batch: 100)).GenerateEmbeddingsAsync(Chunks(5), CancellationToken.None);

        result.Error.Code.Should().Be("Embedding.ResponseMismatch");
    }

    [Fact]
    public async Task GenerateEmbeddings_WhenALaterBatchFails_DiscardsEarlierSuccesses()
    {
        var client = new FakeEmbeddingClient();
        client.ThenSucceed().ThenThrow(400);

        Result<IReadOnlyList<ChunkEmbedding>> result =
            await Create(client, Options(batch: 16)).GenerateEmbeddingsAsync(Chunks(30), CancellationToken.None);

        result.IsFailure.Should().BeTrue("a partially embedded document is worse than none");
    }

    [Fact]
    public async Task GenerateEmbeddings_WhenAuthenticationFails_ReportsItAndDoesNotRetry()
    {
        var client = new FakeEmbeddingClient()
            .ThenThrowException(new AuthenticationFailedException("no identity"));

        Result<IReadOnlyList<ChunkEmbedding>> result =
            await Create(client, Options(batch: 100)).GenerateEmbeddingsAsync(Chunks(3), CancellationToken.None);

        result.Error.Code.Should().Be("Embedding.AuthenticationFailed");
        client.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GenerateEmbeddings_WhenAlreadyCancelled_ThrowsWithoutRetrying()
    {
        var client = new FakeEmbeddingClient().AlwaysSucceed();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        Func<Task> act = () => Create(client, Options(batch: 100)).GenerateEmbeddingsAsync(Chunks(3), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        client.CallCount.Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task GenerateEmbeddings_WhenCancelledMidFlight_DoesNotTreatItAsTransient()
    {
        var client = new FakeEmbeddingClient()
            .ThenThrowException(new OperationCanceledException(), times: 5);

        Func<Task> act = () => Create(client, Options(batch: 100, retries: 4))
            .GenerateEmbeddingsAsync(Chunks(3), CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
        client.CallCount.Should().Be(1, "retrying a cancellation would hold the request open");
    }
}
