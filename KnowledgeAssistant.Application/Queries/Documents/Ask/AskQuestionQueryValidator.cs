using FluentValidation;

namespace KnowledgeAssistant.Application.Queries.Documents.Ask;

/// <summary>
/// The acceptance rules for a question.
/// </summary>
/// <remarks>
/// <para>
/// Every rule sets an explicit error code, for the same reason the upload
/// validator does: without one, FluentValidation supplies the validator's class
/// name, which tells a client nothing and changes if the rule is rewritten.
/// </para>
/// <para>
/// <b>These limits are cost controls, not formatting preferences.</b> Each
/// accepted question spends an embedding call, a search, and a chat completion
/// whose prompt grows with <c>TopK</c>. Rejecting an empty or absurd request
/// before any of that is the cheapest possible place to say no.
/// </para>
/// </remarks>
internal sealed class AskQuestionQueryValidator : AbstractValidator<AskQuestionQuery>
{
    /// <summary>The longest question accepted, in characters.</summary>
    /// <remarks>
    /// Generous for a genuine question and far below anything that would crowd
    /// the model's context. A caller pasting an entire document into the question
    /// field wants ingestion, not retrieval.
    /// </remarks>
    internal const int MaxQuestionLength = 2_000;

    /// <summary>The largest number of chunks a caller may request.</summary>
    /// <remarks>
    /// Bounds prompt size, and with it cost and latency. Beyond roughly this many
    /// chunks a grounded answer stops improving and starts drowning: the relevant
    /// passage competes with twenty near-misses for the model's attention.
    /// </remarks>
    internal const int MaxTopK = 20;

    /// <summary>Initialises the rule set.</summary>
    public AskQuestionQueryValidator()
    {
        RuleFor(query => query.Question)
            .NotEmpty()
            .WithErrorCode("Question.Missing")
            .WithMessage("A question must be supplied.");

        RuleFor(query => query.Question)
            .MaximumLength(MaxQuestionLength)
            .WithErrorCode("Question.TooLong")
            .WithMessage($"A question may be at most {MaxQuestionLength} characters.");

        RuleFor(query => query.TopK)
            .InclusiveBetween(1, MaxTopK)
            .WithErrorCode("Question.InvalidTopK")
            .WithMessage($"topK must be between 1 and {MaxTopK}.");
    }
}
