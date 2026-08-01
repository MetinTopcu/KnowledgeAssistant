using FluentValidation;

namespace KnowledgeAssistant.Application.Commands.Documents.Upload;

/// <summary>
/// The acceptance rules for an uploaded document.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why every rule sets an explicit error code.</b> Without
/// <c>WithErrorCode</c>, FluentValidation supplies the validator's class name
/// (<c>NotEmptyValidator</c>), which tells an API client nothing and changes if
/// the rule is rewritten. A stable code such as <c>Document.InvalidExtension</c>
/// is part of the public contract; the message is not, and may be reworded or
/// localised freely.
/// </para>
/// <para>
/// <b>Why the limits are constants, not options.</b> They are fixed policy
/// today. Promoting them to <c>IOptions&lt;DocumentUploadOptions&gt;</c> is
/// correct the moment they must differ per environment or per tenant — but
/// configuring a value that never varies buys nothing and costs a layer of
/// indirection at every read.
/// </para>
/// </remarks>
internal sealed class UploadDocumentCommandValidator : AbstractValidator<UploadDocumentCommand>
{
    /// <summary>The largest document accepted, in bytes (20 MB).</summary>
    internal const long MaxFileSizeInBytes = 20L * 1024 * 1024;

    /// <summary>The only file extension accepted.</summary>
    internal const string PermittedExtension = ".pdf";

    private const int MaxFileSizeInMegabytes = (int)(MaxFileSizeInBytes / (1024 * 1024));

    /// <summary>Initialises the rule set.</summary>
    public UploadDocumentCommandValidator()
    {
        // "A file exists" reduces to a non-empty name, because the controller maps
        // an absent IFormFile to an empty command. Keeping the check here rather
        // than in the controller means every acceptance rule lives in one file.
        RuleFor(command => command.FileName)
            .NotEmpty()
            .WithErrorCode("Document.FileMissing")
            .WithMessage("A file must be supplied in the 'file' form field.");

        // The remaining rules are conditioned on a file being present, so a
        // missing upload yields one clear error instead of a cascade of four.
        When(command => !string.IsNullOrWhiteSpace(command.FileName), () =>
        {
            RuleFor(command => command.SizeInBytes)
                .GreaterThan(0)
                .WithErrorCode("Document.FileEmpty")
                .WithMessage("The supplied file is empty.");

            RuleFor(command => command.SizeInBytes)
                .LessThanOrEqualTo(MaxFileSizeInBytes)
                .WithErrorCode("Document.FileTooLarge")
                .WithMessage($"The file exceeds the maximum permitted size of {MaxFileSizeInMegabytes} MB.");

            RuleFor(command => command.FileName)
                .Must(HasPermittedExtension)
                .WithErrorCode("Document.InvalidExtension")
                .WithMessage($"Only {PermittedExtension} files are accepted.");
        });
    }

    /// <summary>
    /// Compares the extension case-insensitively so that <c>REPORT.PDF</c> is
    /// accepted — Windows clients routinely send upper-cased extensions, and
    /// rejecting them would be a bug reported as "the upload randomly fails".
    /// </summary>
    private static bool HasPermittedExtension(string fileName) =>
        Path.GetExtension(fileName).Equals(PermittedExtension, StringComparison.OrdinalIgnoreCase);
}
