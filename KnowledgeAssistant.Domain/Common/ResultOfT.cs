namespace KnowledgeAssistant.Domain.Common;

/// <summary>
/// A <see cref="Result"/> that carries a value when it succeeds.
/// </summary>
/// <typeparam name="TValue">The type produced on success.</typeparam>
/// <remarks>
/// Accessing <see cref="Value"/> on a failed result throws. That is deliberate:
/// there is no sensible value to return, and silently handing back
/// <see langword="default"/> would let a null propagate far from the code that
/// caused it. Callers check <see cref="Result.IsSuccess"/> first — and the HTTP
/// mapper in the API layer is the single place that has to remember to.
/// </remarks>
public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    internal Result(TValue? value, bool isSuccess, Error error)
        : base(isSuccess, error)
    {
        _value = value;
    }

    /// <summary>The value produced on success.</summary>
    /// <exception cref="InvalidOperationException">The result is a failure.</exception>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("The value of a failed result cannot be accessed.");

    /// <summary>
    /// Allows a handler to <c>return response;</c> instead of
    /// <c>return Result.Success(response);</c>, keeping the happy path uncluttered.
    /// </summary>
    public static implicit operator Result<TValue>(TValue value) => Success(value);
}
