namespace KnowledgeAssistant.Domain.Common;

/// <summary>
/// The outcome of an operation: either success, or a failure described by an
/// <see cref="Error"/>.
/// </summary>
/// <remarks>
/// <para>
/// Why not exceptions for business flow: an expected outcome such as "this file
/// is too large" is not exceptional. Modelling it as a thrown exception makes it
/// invisible in the method signature, costs a stack-unwind on a routine path,
/// and fills telemetry with noise that hides the failures that do matter.
/// <c>Result&lt;T&gt;</c> puts the failure in the return type, where the compiler
/// keeps the caller honest.
/// </para>
/// <para>
/// Exceptions remain correct for genuinely exceptional conditions — a broken
/// invariant, a lost network. The guards in this class throw for exactly that
/// reason: a success carrying an error is a programming mistake, not a business
/// outcome, and it should fail loudly at the moment of construction.
/// </para>
/// </remarks>
public class Result
{
    /// <summary>Initialises a result, rejecting logically impossible combinations.</summary>
    protected Result(bool isSuccess, Error error)
    {
        switch (isSuccess)
        {
            case true when error != Error.None:
                throw new ArgumentException("A successful result cannot carry an error.", nameof(error));
            case false when error == Error.None:
                throw new ArgumentException("A failed result must carry an error.", nameof(error));
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    /// <summary>Whether the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>Whether the operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// The failure, or <see cref="Common.Error.None"/> when <see cref="IsSuccess"/>
    /// is <see langword="true"/>. Never null.
    /// </summary>
    public Error Error { get; }

    /// <summary>Creates a successful result with no value.</summary>
    public static Result Success() => new(true, Error.None);

    /// <summary>Creates a failed result.</summary>
    public static Result Failure(Error error) => new(false, error);

    /// <summary>Creates a successful result carrying a value.</summary>
    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);

    /// <summary>Creates a failed result of a value-bearing type.</summary>
    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);
}
