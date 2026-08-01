namespace KnowledgeAssistant.Application.Interfaces;

/// <summary>
/// What a model consumed and produced answering one request.
/// </summary>
/// <param name="PromptTokens">Tokens billed for the input.</param>
/// <param name="CompletionTokens">Tokens billed for the generated answer.</param>
/// <param name="TotalTokens">The sum the provider reports.</param>
/// <remarks>
/// Surfaced because tokens are the unit this system is billed in, and an answer
/// whose cost is invisible cannot be budgeted, rate-limited, or attributed. The
/// total is taken from the provider rather than added up locally: providers count
/// tokens the caller cannot see, and their number is the one on the invoice.
/// </remarks>
public sealed record TokenUsage(
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens);

/// <summary>
/// A model's answer to a chat completion request.
/// </summary>
/// <param name="Content">The generated text.</param>
/// <param name="Usage">
/// Token counts, or <see langword="null"/> when the provider did not report them.
/// </param>
/// <remarks>
/// <paramref name="Usage"/> is nullable because reporting it is a provider
/// courtesy rather than a guarantee. Modelling it as non-null would mean
/// inventing zeros when it is absent, and a zero token count is
/// indistinguishable from a free call in whatever dashboard consumes it.
/// </remarks>
public sealed record ChatCompletionResult(
    string Content,
    TokenUsage? Usage);
