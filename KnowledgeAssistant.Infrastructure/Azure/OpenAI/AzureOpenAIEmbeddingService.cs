using System.ClientModel;
using Azure.Identity;
using KnowledgeAssistant.Application.Interfaces;
using KnowledgeAssistant.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Embeddings;
using Polly;

namespace KnowledgeAssistant.Infrastructure.Azure.OpenAI;

/// <summary>
/// Generates embeddings using an Azure OpenAI deployment.
/// </summary>
/// <remarks>
/// <para>
/// <b>Registered as a singleton.</b> <see cref="EmbeddingClient"/> is thread-safe
/// and holds a pooled connection and a cached token, and the resilience pipeline
/// is immutable and reusable.
/// </para>
/// <para>
/// <b>Both public methods route through one private path.</b> Embedding a corpus
/// and embedding a question are the same request with different callers, and the
/// batching, retry, order verification, and dimension check must be identical for
/// both — because a question embedded differently from the corpus produces a
/// search that returns confident nonsense rather than an error.
/// </para>
/// <para>
/// <b>Batches are sent one after another, not in parallel.</b> Concurrency would
/// multiply the request rate against a quota that is already the most common
/// cause of failure. If throughput becomes the constraint, the answer is a larger
/// deployment or a queue, not more simultaneous requests from one caller.
/// </para>
/// </remarks>
internal sealed partial class AzureOpenAIEmbeddingService : IEmbeddingService
{
    private readonly EmbeddingClient _embeddingClient;
    private readonly AzureOpenAIOptions _options;
    private readonly ResiliencePipeline _resiliencePipeline;
    private readonly ILogger<AzureOpenAIEmbeddingService> _logger;

    /// <summary>Initialises the service.</summary>
    public AzureOpenAIEmbeddingService(
        EmbeddingClient embeddingClient,
        IOptions<AzureOpenAIOptions> options,
        ILogger<AzureOpenAIEmbeddingService> logger)
    {
        ArgumentNullException.ThrowIfNull(embeddingClient);
        ArgumentNullException.ThrowIfNull(options);

        _embeddingClient = embeddingClient;
        _options = options.Value;
        _logger = logger;
        _resiliencePipeline = OpenAIResiliencePipeline.Create(_options, logger, "embedding");
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ChunkEmbedding>>> GenerateEmbeddingsAsync(
        IReadOnlyList<DocumentChunk> chunks,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        if (chunks.Count == 0)
        {
            return Result.Success<IReadOnlyList<ChunkEmbedding>>([]);
        }

        var texts = new string[chunks.Count];

        for (int index = 0; index < chunks.Count; index++)
        {
            texts[index] = chunks[index].Text;
        }

        Result<ReadOnlyMemory<float>[]> vectors = await EmbedTextsAsync(texts, cancellationToken)
            .ConfigureAwait(false);

        if (vectors.IsFailure)
        {
            return Result.Failure<IReadOnlyList<ChunkEmbedding>>(vectors.Error);
        }

        // Safe to pair by position: EmbedTextsAsync returns one vector per input in
        // request order, and verifies that count itself before returning.
        var embeddings = new ChunkEmbedding[chunks.Count];

        for (int index = 0; index < chunks.Count; index++)
        {
            embeddings[index] = new ChunkEmbedding(chunks[index].ChunkId, vectors.Value[index]);
        }

        return Result.Success<IReadOnlyList<ChunkEmbedding>>(embeddings);
    }

    /// <inheritdoc />
    public async Task<Result<ReadOnlyMemory<float>>> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        Result<ReadOnlyMemory<float>[]> vectors = await EmbedTextsAsync([text], cancellationToken)
            .ConfigureAwait(false);

        return vectors.IsFailure
            ? Result.Failure<ReadOnlyMemory<float>>(vectors.Error)
            : Result.Success(vectors.Value[0]);
    }

    /// <summary>
    /// Embeds every text, in batches, and returns the vectors in request order.
    /// </summary>
    /// <remarks>
    /// The single implementation both public methods delegate to. Everything that
    /// must not differ between embedding a corpus and embedding a query — retry
    /// policy, batch size, order verification, dimension validation — lives here
    /// exactly once.
    /// </remarks>
    private async Task<Result<ReadOnlyMemory<float>[]>> EmbedTextsAsync(
        string[] texts,
        CancellationToken cancellationToken)
    {
        var vectors = new ReadOnlyMemory<float>[texts.Length];
        int batchSize = _options.EmbeddingBatchSize;

        for (int offset = 0; offset < texts.Length; offset += batchSize)
        {
            int count = Math.Min(batchSize, texts.Length - offset);

            Result batchResult = await EmbedBatchAsync(texts, offset, count, vectors, cancellationToken)
                .ConfigureAwait(false);

            if (batchResult.IsFailure)
            {
                // Everything gathered so far is discarded. A partially embedded
                // document is worse than none: indexing it leaves the document
                // silently incomplete.
                return Result.Failure<ReadOnlyMemory<float>[]>(batchResult.Error);
            }
        }

        LogEmbeddingsGenerated(texts.Length, (texts.Length + batchSize - 1) / batchSize);

        return vectors;
    }

    /// <summary>Embeds one batch and writes the vectors into <paramref name="destination"/>.</summary>
    private async Task<Result> EmbedBatchAsync(
        string[] texts,
        int offset,
        int count,
        ReadOnlyMemory<float>[] destination,
        CancellationToken cancellationToken)
    {
        var inputs = new string[count];

        for (int index = 0; index < count; index++)
        {
            inputs[index] = texts[offset + index];
        }

        OpenAIEmbeddingCollection collection;

        try
        {
            collection = await _resiliencePipeline.ExecuteAsync(
                async token =>
                {
                    ClientResult<OpenAIEmbeddingCollection> result = await _embeddingClient
                        .GenerateEmbeddingsAsync(inputs, options: null, token)
                        .ConfigureAwait(false);

                    return result.Value;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (ClientResultException exception) when (exception.Status == 429)
        {
            LogRateLimited(exception, offset, count);
            return Result.Failure(EmbeddingErrors.RateLimited);
        }
        catch (ClientResultException exception)
        {
            LogGenerationFailed(exception, offset, count, exception.Status);
            return Result.Failure(EmbeddingErrors.GenerationFailed);
        }
        // Covers CredentialUnavailableException too, which derives from it.
        catch (AuthenticationFailedException exception)
        {
            LogAuthenticationFailed(exception);
            return Result.Failure(EmbeddingErrors.AuthenticationFailed);
        }
        catch (HttpRequestException exception)
        {
            LogTransportFailed(exception);
            return Result.Failure(EmbeddingErrors.GenerationFailed);
        }

        return MapBatch(offset, count, collection, destination);
    }

    /// <summary>
    /// Copies each returned vector into its input's slot.
    /// </summary>
    /// <remarks>
    /// <b>Position is the only available correspondence.</b> The SDK's returned
    /// embedding type exposes no index, so this relies on the service returning
    /// results in request order — which the API contract guarantees. The count
    /// check is what keeps that reliance honest: it is the only externally visible
    /// symptom if the assumption ever stops holding, and a wrong pairing is
    /// undetectable everywhere downstream.
    /// </remarks>
    private Result MapBatch(
        int offset,
        int count,
        OpenAIEmbeddingCollection collection,
        ReadOnlyMemory<float>[] destination)
    {
        int received = 0;

        foreach (OpenAIEmbedding embedding in collection)
        {
            if (received >= count)
            {
                LogResponseMismatch(offset, count, received + 1);
                return Result.Failure(EmbeddingErrors.ResponseMismatch);
            }

            ReadOnlyMemory<float> vector = embedding.ToFloats();

            if (_options.EmbeddingDimensions > 0 && vector.Length != _options.EmbeddingDimensions)
            {
                LogDimensionMismatch(_options.EmbeddingDimensions, vector.Length);
                return Result.Failure(EmbeddingErrors.DimensionMismatch);
            }

            destination[offset + received] = vector;
            received++;
        }

        if (received != count)
        {
            LogResponseMismatch(offset, count, received);
            return Result.Failure(EmbeddingErrors.ResponseMismatch);
        }

        return Result.Success();
    }

    [LoggerMessage(
        EventId = 5000,
        Level = LogLevel.Information,
        Message = "Generated embeddings for {InputCount} inputs in {RequestCount} requests.")]
    private partial void LogEmbeddingsGenerated(int inputCount, int requestCount);

    [LoggerMessage(
        EventId = 5001,
        Level = LogLevel.Error,
        Message = "Embedding request failed for inputs {Offset}..{Count} with status {Status}.")]
    private partial void LogGenerationFailed(Exception exception, int offset, int count, int status);

    [LoggerMessage(
        EventId = 5002,
        Level = LogLevel.Error,
        Message = "Embedding deployment is rate limited; retries were exhausted for inputs {Offset}..{Count}.")]
    private partial void LogRateLimited(Exception exception, int offset, int count);

    [LoggerMessage(
        EventId = 5003,
        Level = LogLevel.Error,
        Message = "Failed to authenticate to Azure OpenAI. Verify the managed identity and its RBAC role assignments.")]
    private partial void LogAuthenticationFailed(Exception exception);

    [LoggerMessage(
        EventId = 5004,
        Level = LogLevel.Error,
        Message = "Azure OpenAI was unreachable after all retries were exhausted.")]
    private partial void LogTransportFailed(Exception exception);

    [LoggerMessage(
        EventId = 5005,
        Level = LogLevel.Error,
        Message = "Embedding response mismatch: sent {Count} inputs from offset {Offset} but received {Received}.")]
    private partial void LogResponseMismatch(int offset, int count, int received);

    [LoggerMessage(
        EventId = 5006,
        Level = LogLevel.Error,
        Message = "Embedding dimension mismatch: configured {Expected} but the deployment returned {Actual}. " +
                  "The deployment is serving a different model than the configuration expects.")]
    private partial void LogDimensionMismatch(int expected, int actual);
}
