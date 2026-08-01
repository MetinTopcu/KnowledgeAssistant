namespace KnowledgeAssistant.Domain.Common;

/// <summary>
/// A failure expressed as a value rather than a thrown exception.
/// </summary>
/// <param name="Code">
/// A stable, machine-readable identifier (for example <c>Document.InvalidExtension</c>).
/// Clients branch on this; they must never branch on <paramref name="Description"/>.
/// </param>
/// <param name="Description">A human-readable explanation, safe to show to an API consumer.</param>
/// <param name="Type">The classification used to derive an HTTP status code.</param>
/// <remarks>
/// A <c>record</c> gives structural equality for free, which is what makes
/// <c>result.Error == UploadDocumentErrors.FileTooLarge</c> and the
/// <see cref="None"/> sentinel comparison work.
/// <para>
/// Not sealed: <see cref="ValidationError"/> extends it to carry several
/// failures at once.
/// </para>
/// </remarks>
public record Error(string Code, string Description, ErrorType Type)
{
    /// <summary>
    /// The absence of an error. Used as the <see cref="Result.Error"/> of a
    /// successful result so that the property is never null and callers never
    /// need a null check.
    /// </summary>
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);

    /// <summary>Creates an error representing invalid input.</summary>
    public static Error Validation(string code, string description) =>
        new(code, description, ErrorType.Validation);

    /// <summary>Creates an error representing an unexpected failure.</summary>
    public static Error Failure(string code, string description) =>
        new(code, description, ErrorType.Failure);
}
