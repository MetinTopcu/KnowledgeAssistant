using System.ClientModel;
using System.ClientModel.Primitives;
using System.Globalization;
using Azure.Identity;
using KnowledgeAssistant.Application.Interfaces;
using KnowledgeAssistant.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Embeddings;
using Polly;
using Polly.Retry;

namespace KnowledgeAssistant.Infrastructure.Azure.OpenAI;

/// <summary>
/// Generates embeddings using an Azure OpenAI deployment.
/// </summary>
/// <remarks>
/// <para>
/// <b>Registered as a singleton.</b> <see cref="EmbeddingClient"/> is thread-safe
/// and holds a pooled connection and a cached token, and the resilience pipeline
/// built in the constructor is immutable and reusable. Building either per
/// request would re-run the credential chain and discard the retry state that
/// makes backoff meaningful.
/// </para>
/// <para>
/// <b>Batches are sent one after another, not in parallel.</b> Concurrency here
/// would multiply the request rate against a quota that is already the most
/// common cause of failure, converting a slow ingestion into a failing one. If
/// throughput becomes the constraint, the answer is a larger deployment or a
/// queue, not more simultaneous requests from a single caller.
/// </para>
/// </remarks>
internal sealed partial class AzureOpenAIEmbeddingService : IEmbeddingService
{
    /// <summary>HTTP statuses worth trying again.</summary>
    /// <remarks>
    /// 408 and 5xx are transport or server faults that frequently succeed on a
    /// second attempt. 429 is the rate limit, and is the reason this list exists
    /// at all. Everything else — 400, 401, 404 — is deterministic: the request,
    /// the credential, or the deployment name is wrong, and repeating it only
    /// wastes the caller's time before failing identically.
    /// </remarks>
    private static readonly int[] TransientStatuses = [408, 429, 500, 502, 503, 504];

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
        _resiliencePipeline = BuildResiliencePipeline();
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

        var embeddings = new List<ChunkEmbedding>(chunks.Count);
        int batchSize = _options.EmbeddingBatchSize;

        for (int offset = 0; offset < chunks.Count; offset += batchSize)
        {
            int count = Math.Min(batchSize, chunks.Count - offset);

            Result batchResult = await EmbedBatchAsync(chunks, offset, count, embeddings, cancellationToken)
                .ConfigureAwait(false);

            if (batchResult.IsFailure)
            {
                // Everything accumulated so far is discarded. See the port's
                // remarks: a partially embedded document is worse than none.
                return Result.Failure<IReadOnlyList<ChunkEmbedding>>(batchResult.Error);
            }
        }

        LogEmbeddingsGenerated(chunks.Count, (chunks.Count + batchSize - 1) / batchSize);

        return Result.Success<IReadOnlyList<ChunkEmbedding>>(embeddings);
    }

    /// <summary>
    /// Embeds one batch and appends the results to <paramref name="destination"/>.
    /// </summary>
    private async Task<Result> EmbedBatchAsync(
        IReadOnlyList<DocumentChunk> chunks,
        int offset,
        int count,
        List<ChunkEmbedding> destination,
        CancellationToken cancellationToken)
    {
        var inputs = new string[count];

        for (int index = 0; index < count; index++)
        {
            inputs[index] = chunks[offset + index].Text;
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
        // Covers CredentialUnavailableException too, which derives from it: the
        // credential chain finding no identity and the chain failing to redeem one
        // are the same problem to a caller, and the log carries the distinction.
        catch (AuthenticationFailedException exception)
        {
            LogAuthenticationFailed(exception);
            return Result.Failure(EmbeddingErrors.AuthenticationFailed);
        }
        catch (HttpRequestException exception)
        {
            // The transport never reached the service. Distinct from a
            // ClientResultException, which means the service answered.
            LogTransportFailed(exception);
            return Result.Failure(EmbeddingErrors.GenerationFailed);
        }

        return MapBatch(chunks, offset, count, collection, destination);
    }

    /// <summary>
    /// Pairs each returned vector with the chunk it was generated from.
    /// </summary>
    /// <remarks>
    /// <b>Position is the only available correspondence.</b> The SDK's returned
    /// embedding type exposes no index, so this relies on the service returning
    /// results in request order — which the API contract guarantees. The count
    /// check below is what keeps that reliance honest: it is the only externally
    /// visible symptom if the assumption ever stops holding, and a wrong pairing
    /// is undetectable everywhere downstream.
    /// </remarks>
    private Result MapBatch(
        IReadOnlyList<DocumentChunk> chunks,
        int offset,
        int count,
        OpenAIEmbeddingCollection collection,
        List<ChunkEmbedding> destination)
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

            destination.Add(new ChunkEmbedding(chunks[offset + received].ChunkId, vector));
            received++;
        }

        if (received != count)
        {
            LogResponseMismatch(offset, count, received);
            return Result.Failure(EmbeddingErrors.ResponseMismatch);
        }

        return Result.Success();
    }

    /// <summary>
    /// Builds the retry pipeline that wraps every embedding request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the only retry in play.</b> The client is deliberately
    /// registered with its own retry disabled — see the DI registration. Two
    /// retry layers multiply rather than add, so four attempts here over three in
    /// the SDK would be twelve requests to a deployment that is most likely
    /// failing because it is already receiving too many.
    /// </para>
    /// <para>
    /// <b>Cancellation is never retried.</b> <see cref="OperationCanceledException"/>
    /// is absent from the predicate on purpose: a caller who has disconnected
    /// wants the work abandoned, and retrying their cancellation would hold the
    /// request open for the full backoff schedule.
    /// </para>
    /// <para>
    /// <b>Jitter is enabled</b> because without it every worker that hit the same
    /// rate limit retries at the same instant, reproducing the burst that caused
    /// the limit and turning one 429 into a synchronised cycle of them.
    /// </para>
    /// </remarks>
    private ResiliencePipeline BuildResiliencePipeline() =>
        new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<ClientResultException>(exception => Array.IndexOf(TransientStatuses, exception.Status) >= 0)
                    .Handle<HttpRequestException>(),
                MaxRetryAttempts = _options.MaxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromSeconds(_options.RetryBaseDelaySeconds),
                MaxDelay = TimeSpan.FromSeconds(_options.RetryMaxDelaySeconds),

                // Returning null defers to the exponential schedule above. A value
                // is returned only when the service told us how long to wait, in
                // which case guessing is strictly worse than obeying.
                DelayGenerator = arguments => ValueTask.FromResult(
                    GetRetryAfter(arguments.Outcome.Exception, _options.RetryMaxDelaySeconds)),

                OnRetry = arguments =>
                {
                    LogRetrying(
                        arguments.AttemptNumber + 1,
                        _options.MaxRetryAttempts,
                        arguments.RetryDelay.TotalMilliseconds,
                        (arguments.Outcome.Exception as ClientResultException)?.Status ?? 0);

                    return ValueTask.CompletedTask;
                },
            })
            .Build();

    /// <summary>
    /// Reads a <c>Retry-After</c> header, if the service supplied one.
    /// </summary>
    /// <remarks>
    /// The header comes in two forms — delay in seconds, or an HTTP date — and
    /// Azure OpenAI uses the former for rate limits. Both are handled, and both
    /// are clamped: an unbounded wait taken on a service's word would pin a
    /// request thread for as long as that service cared to name.
    /// </remarks>
    private static TimeSpan? GetRetryAfter(Exception? exception, double maximumSeconds)
    {
        if (exception is not ClientResultException clientException)
        {
            return null;
        }

        PipelineResponse? response = clientException.GetRawResponse();

        if (response is null ||
            !response.Headers.TryGetValue("retry-after", out string? headerValue) ||
            string.IsNullOrWhiteSpace(headerValue))
        {
            return null;
        }

        var maximum = TimeSpan.FromSeconds(maximumSeconds);

        if (double.TryParse(headerValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
        {
            return seconds <= 0 ? TimeSpan.Zero : Min(TimeSpan.FromSeconds(seconds), maximum);
        }

        if (DateTimeOffset.TryParse(
                headerValue,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset retryAt))
        {
            TimeSpan delay = retryAt - DateTimeOffset.UtcNow;
            return delay <= TimeSpan.Zero ? TimeSpan.Zero : Min(delay, maximum);
        }

        return null;
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;

    [LoggerMessage(
        EventId = 5000,
        Level = LogLevel.Information,
        Message = "Generated embeddings for {ChunkCount} chunks in {RequestCount} requests.")]
    private partial void LogEmbeddingsGenerated(int chunkCount, int requestCount);

    [LoggerMessage(
        EventId = 5001,
        Level = LogLevel.Error,
        Message = "Embedding request failed for chunks {Offset}..{Count} with status {Status}.")]
    private partial void LogGenerationFailed(Exception exception, int offset, int count, int status);

    [LoggerMessage(
        EventId = 5002,
        Level = LogLevel.Error,
        Message = "Embedding deployment is rate limited; retries were exhausted for chunks {Offset}..{Count}.")]
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

    [LoggerMessage(
        EventId = 5007,
        Level = LogLevel.Warning,
        Message = "Retrying embedding request (attempt {Attempt} of {MaxAttempts}) after {DelayMs}ms; last status {Status}.")]
    private partial void LogRetrying(int attempt, int maxAttempts, double delayMs, int status);
}
