using FluentValidation;

namespace KnowledgeAssistant.Application.Queries.Documents.AskAgent;

/// <summary>
/// The acceptance rules for a question put to the agent.
/// </summary>
/// <remarks>
/// <para>
/// The limits match the retrieval slice's deliberately: the same question should
/// be accepted or rejected identically whichever way it is answered, and a caller
/// discovering that one endpoint accepts a longer question than the other has
/// found a bug rather than a feature.
/// </para>
/// <para>
/// <b>These are cost controls, and they bind harder here.</b> An accepted question
/// spends at least one model call, and an agent may spend several plus a retrieval
/// for each. Rejecting an empty or absurd request before any of that is the
/// cheapest possible place to say no — and the only place where the cost of saying
/// no is known in advance.
/// </para>
/// </remarks>
internal sealed class AskAgentQueryValidator : AbstractValidator<AskAgentQuery>
{
    /// <summary>The longest question accepted, in characters.</summary>
    internal const int MaxQuestionLength = 2_000;

    /// <summary>The largest number of passages one search may return.</summary>
    /// <remarks>
    /// Bounds a single search, and the adapter's own iteration ceiling bounds how
    /// many searches there can be. Both are needed: either one alone leaves the
    /// product of the two unbounded.
    /// </remarks>
    internal const int MaxSourcesPerSearch = 20;

    /// <summary>Initialises the rule set.</summary>
    public AskAgentQueryValidator()
    {
        RuleFor(query => query.Question)
            .NotEmpty()
            .WithErrorCode("Question.Missing")
            .WithMessage("A question must be supplied.");

        RuleFor(query => query.Question)
            .MaximumLength(MaxQuestionLength)
            .WithErrorCode("Question.TooLong")
            .WithMessage($"A question may be at most {MaxQuestionLength} characters.");

        RuleFor(query => query.MaxSources)
            .InclusiveBetween(1, MaxSourcesPerSearch)
            .WithErrorCode("Question.InvalidMaxSources")
            .WithMessage($"maxSources must be between 1 and {MaxSourcesPerSearch}.");
    }
}
