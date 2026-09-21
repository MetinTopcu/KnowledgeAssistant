using System.ClientModel;
using Azure.Identity;
using KnowledgeAssistant.Application.Interfaces;
using KnowledgeAssistant.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using Polly;
using ApplicationChatMessage = KnowledgeAssistant.Application.Interfaces.ChatMessage;
using OpenAIChatMessage = OpenAI.Chat.ChatMessage;

namespace KnowledgeAssistant.Infrastructure.Azure.OpenAI;

/// <summary>
/// Generates answers using an Azure OpenAI chat deployment.
/// </summary>
/// <remarks>
/// <para>
/// <b>Registered as a singleton.</b> <see cref="ChatClient"/> is thread-safe and
/// holds a pooled connection and a cached token, and the resilience pipeline is
/// immutable and reusable. Building either per request would re-run the
/// credential chain and discard the retry state that makes backoff meaningful.
/// </para>
/// <para>
/// <b>The retry policy is the embedding adapter's, shared rather than copied.</b>
/// Chat calls fail the same ways embedding calls do — 429 above all — and a
/// second policy would drift from the first the moment either was tuned.
/// </para>
/// <para>
/// <b>It knows nothing about retrieval.</b> No prompt, no grounding, no
/// citations: this adapter translates messages to the SDK's types, calls the
/// deployment, and translates the answer back. Everything that makes an answer
/// grounded lives in the use case, where changing it does not mean touching a
/// vendor adapter.
/// </para>
/// </remarks>
internal sealed partial class AzureOpenAIChatService : IChatService
{
    private readonly ChatClient _chatClient;
    private readonly AzureOpenAIOptions _options;
    private readonly ResiliencePipeline _resiliencePipeline;
    private readonly ILogger<AzureOpenAIChatService> _logger;

    /// <summary>Initialises the service.</summary>
    public AzureOpenAIChatService(
        ChatClient chatClient,
        IOptions<AzureOpenAIOptions> options,
        ILogger<AzureOpenAIChatService> logger)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(options);

        _chatClient = chatClient;
        _options = options.Value;
        _logger = logger;
        _resiliencePipeline = OpenAIResiliencePipeline.Create(_options, logger, "chat");
    }

    /// <inheritdoc />
    public async Task<Result<ChatCompletionResult>> CompleteAsync(
        IReadOnlyList<ApplicationChatMessage> messages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);

        if (messages.Count == 0)
        {
            throw new ArgumentException("At least one message is required.", nameof(messages));
        }

        var requestMessages = new OpenAIChatMessage[messages.Count];

        for (int index = 0; index < messages.Count; index++)
        {
            requestMessages[index] = ToSdkMessage(messages[index]);
        }

        var completionOptions = new ChatCompletionOptions
        {
            MaxOutputTokenCount = _options.ChatMaxOutputTokens,

            // Null leaves the parameter out of the request, so the model's own
            // default applies. Reasoning models reject any explicit value but
            // that default; see AzureOpenAIOptions.ChatTemperature.
            Temperature = (float?)_options.ChatTemperature,
        };

        ChatCompletion completion;

        try
        {
            completion = await _resiliencePipeline.ExecuteAsync(
                async token =>
                {
                    ClientResult<ChatCompletion> result = await _chatClient
                        .CompleteChatAsync(requestMessages, completionOptions, token)
                        .ConfigureAwait(false);

                    return result.Value;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (ClientResultException exception) when (exception.Status == 429)
        {
            LogRateLimited(exception);
            return Result.Failure<ChatCompletionResult>(ChatErrors.RateLimited);
        }
        catch (ClientResultException exception)
        {
            LogCompletionFailed(exception, exception.Status);
            return Result.Failure<ChatCompletionResult>(ChatErrors.CompletionFailed);
        }
        // Covers CredentialUnavailableException too, which derives from it.
        catch (AuthenticationFailedException exception)
        {
            LogAuthenticationFailed(exception);
            return Result.Failure<ChatCompletionResult>(ChatErrors.AuthenticationFailed);
        }
        catch (HttpRequestException exception)
        {
            LogTransportFailed(exception);
            return Result.Failure<ChatCompletionResult>(ChatErrors.CompletionFailed);
        }

        // A refusal is a content-filter decision, and it arrives as a successful
        // response with no content. Reported distinctly so the log says the model
        // declined rather than that the call failed.
        if (!string.IsNullOrEmpty(completion.Refusal))
        {
            LogRefused(completion.FinishReason.ToString());
            return Result.Failure<ChatCompletionResult>(ChatErrors.NoAnswerGenerated);
        }

        string answer = ExtractText(completion);

        if (string.IsNullOrWhiteSpace(answer))
        {
            LogEmptyCompletion(completion.FinishReason.ToString());
            return Result.Failure<ChatCompletionResult>(ChatErrors.NoAnswerGenerated);
        }

        // A completion cut off at the token ceiling is still returned — a truncated
        // grounded answer is usually more useful than none — but it is logged,
        // because the fix is configuration rather than a retry.
        if (completion.FinishReason == ChatFinishReason.Length)
        {
            LogTruncated(_options.ChatMaxOutputTokens);
        }

        int totalTokens = completion.Usage?.TotalTokenCount ?? 0;
        string finishReason = completion.FinishReason.ToString();

        LogCompletionSucceeded(answer.Length, totalTokens, finishReason);

        return new ChatCompletionResult(answer, ToTokenUsage(completion.Usage));
    }

    /// <summary>Maps a port message onto the SDK's message hierarchy.</summary>
    /// <remarks>
    /// The SDK models roles as distinct types rather than an enum, so this switch
    /// is the translation the port's vendor-neutral <see cref="ChatRole"/> exists
    /// to require. Assistant messages are accepted for completeness of the
    /// mapping; nothing in this system sends one.
    /// </remarks>
    private static OpenAIChatMessage ToSdkMessage(ApplicationChatMessage message) => message.Role switch
    {
        ChatRole.System => new SystemChatMessage(message.Content),
        ChatRole.User => new UserChatMessage(message.Content),
        ChatRole.Assistant => new AssistantChatMessage(message.Content),
        _ => throw new ArgumentOutOfRangeException(
            nameof(message),
            message.Role,
            "Unknown chat role."),
    };

    /// <summary>
    /// Concatenates the text parts of a completion.
    /// </summary>
    /// <remarks>
    /// <c>Content</c> is a list of parts, not a string: a response can arrive as
    /// several text segments, and can also carry non-text parts this system does
    /// not request. Taking only the first part would silently truncate a
    /// multi-part answer, so the text parts are joined and the rest ignored.
    /// </remarks>
    private static string ExtractText(ChatCompletion completion)
    {
        if (completion.Content.Count == 1)
        {
            return completion.Content[0].Text ?? string.Empty;
        }

        return string.Concat(completion.Content
            .Where(part => part.Kind == ChatMessageContentPartKind.Text)
            .Select(part => part.Text));
    }

    /// <summary>Maps SDK token counts onto the port's vendor-neutral record.</summary>
    private static TokenUsage? ToTokenUsage(ChatTokenUsage? usage) =>
        usage is null
            ? null
            : new TokenUsage(usage.InputTokenCount, usage.OutputTokenCount, usage.TotalTokenCount);

    [LoggerMessage(
        EventId = 5200,
        Level = LogLevel.Information,
        Message = "Chat completion returned {AnswerLength} characters using {TotalTokens} tokens (finish reason {FinishReason}).")]
    private partial void LogCompletionSucceeded(int answerLength, int totalTokens, string finishReason);

    [LoggerMessage(
        EventId = 5201,
        Level = LogLevel.Error,
        Message = "Chat completion failed with status {Status}.")]
    private partial void LogCompletionFailed(Exception exception, int status);

    [LoggerMessage(
        EventId = 5202,
        Level = LogLevel.Error,
        Message = "Chat deployment is rate limited; retries were exhausted.")]
    private partial void LogRateLimited(Exception exception);

    [LoggerMessage(
        EventId = 5203,
        Level = LogLevel.Error,
        Message = "Failed to authenticate to Azure OpenAI. Verify the managed identity and its RBAC role assignments.")]
    private partial void LogAuthenticationFailed(Exception exception);

    [LoggerMessage(
        EventId = 5204,
        Level = LogLevel.Error,
        Message = "Azure OpenAI was unreachable after all retries were exhausted.")]
    private partial void LogTransportFailed(Exception exception);

    [LoggerMessage(
        EventId = 5205,
        Level = LogLevel.Warning,
        Message = "The chat deployment refused to answer (finish reason {FinishReason}).")]
    private partial void LogRefused(string finishReason);

    [LoggerMessage(
        EventId = 5206,
        Level = LogLevel.Warning,
        Message = "The chat deployment returned no text (finish reason {FinishReason}).")]
    private partial void LogEmptyCompletion(string finishReason);

    [LoggerMessage(
        EventId = 5207,
        Level = LogLevel.Warning,
        Message = "The answer was truncated at the {MaxOutputTokens}-token ceiling. Raise Azure:AiFoundry:ChatMaxOutputTokens if answers are being cut short.")]
    private partial void LogTruncated(int maxOutputTokens);
}
