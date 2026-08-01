using System.ClientModel;
using System.ClientModel.Primitives;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace KnowledgeAssistant.Infrastructure.Azure.OpenAI;

/// <summary>
/// Builds the retry pipeline that wraps every Azure OpenAI call.
/// </summary>
/// <remarks>
/// <para>
/// <b>Extracted from the embedding adapter so the chat adapter shares it rather
/// than restating it.</b> Two copies of a retry policy do not stay identical:
/// one gains a status code or a longer ceiling and the other does not, and the
/// difference is invisible until a deployment starts throttling and only half
/// the calls cope.
/// </para>
/// <para>
/// <b>This is the only retry in play.</b> Both clients are registered with the
/// SDK's own retry disabled. Two layers compose multiplicatively, so four
/// attempts here over three in the SDK would be twelve requests to a deployment
/// that is most likely failing because it is already receiving too many.
/// </para>
/// <para>
/// <b>Cancellation is never retried.</b> <see cref="OperationCanceledException"/>
/// is absent from the predicate on purpose: a caller who has disconnected wants
/// the work abandoned, not repeated on a backoff schedule.
/// </para>
/// </remarks>
internal static partial class OpenAIResiliencePipeline
{
    /// <summary>HTTP statuses worth trying again.</summary>
    /// <remarks>
    /// 408 and 5xx are transport or server faults that frequently succeed on a
    /// second attempt. 429 is the rate limit, and is the reason this list exists.
    /// Everything else — 400, 401, 404 — is deterministic: the request, the
    /// credential, or the deployment name is wrong, and repeating it only wastes
    /// the caller's time before failing identically.
    /// </remarks>
    private static readonly int[] TransientStatuses = [408, 429, 500, 502, 503, 504];

    /// <summary>Creates the pipeline for one client.</summary>
    /// <param name="options">Supplies the retry budget and delay ceiling.</param>
    /// <param name="logger">Receives a warning for each retry attempt.</param>
    /// <param name="operation">
    /// Names the caller in the retry log — "embedding" or "chat" — so a burst of
    /// retries can be attributed without correlating timestamps.
    /// </param>
    internal static ResiliencePipeline Create(
        AzureOpenAIOptions options,
        ILogger logger,
        string operation)
    {
        double maximumSeconds = options.RetryMaxDelaySeconds;

        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<ClientResultException>(exception => Array.IndexOf(TransientStatuses, exception.Status) >= 0)
                    .Handle<HttpRequestException>(),
                MaxRetryAttempts = options.MaxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,

                // Without jitter every worker that hit the same rate limit retries
                // at the same instant, reproducing the burst that caused the limit
                // and turning one 429 into a synchronised cycle of them.
                UseJitter = true,
                Delay = TimeSpan.FromSeconds(options.RetryBaseDelaySeconds),
                MaxDelay = TimeSpan.FromSeconds(maximumSeconds),

                // Returning null defers to the exponential schedule. A value is
                // returned only when the service told us how long to wait, in
                // which case guessing is strictly worse than obeying.
                DelayGenerator = arguments => ValueTask.FromResult(
                    GetRetryAfter(arguments.Outcome.Exception, maximumSeconds)),

                OnRetry = arguments =>
                {
                    LogRetrying(
                        logger,
                        operation,
                        arguments.AttemptNumber + 1,
                        options.MaxRetryAttempts,
                        arguments.RetryDelay.TotalMilliseconds,
                        (arguments.Outcome.Exception as ClientResultException)?.Status ?? 0);

                    return ValueTask.CompletedTask;
                },
            })
            .Build();
    }

    /// <summary>
    /// Reads a <c>Retry-After</c> header, if the service supplied one.
    /// </summary>
    /// <remarks>
    /// The header comes in two forms — a delay in seconds, or an HTTP date — and
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
        EventId = 5100,
        Level = LogLevel.Warning,
        Message = "Retrying {Operation} request (attempt {Attempt} of {MaxAttempts}) after {DelayMs}ms; last status {Status}.")]
    private static partial void LogRetrying(
        ILogger logger, string operation, int attempt, int maxAttempts, double delayMs, int status);
}
