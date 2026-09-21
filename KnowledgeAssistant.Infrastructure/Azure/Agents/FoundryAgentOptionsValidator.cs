using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace KnowledgeAssistant.Infrastructure.Azure.Agents;

/// <summary>
/// Requires a pinned agent version everywhere except Development.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why an <see cref="IValidateOptions{TOptions}"/>.</b> The rule depends on
/// <see cref="IHostEnvironment"/>, which a data-annotations context cannot
/// supply — the same reason <c>BlobStorageOptionsValidator</c> exists. The
/// attribute and <c>IValidatableObject</c> checks on
/// <see cref="FoundryAgentOptions"/> still run alongside this one.
/// </para>
/// <para>
/// <b>Why Development is the exception.</b> An unpinned agent is provisioned by
/// the running process on the first question. That is how a version comes to
/// exist in the first place, so a developer needs it; a deployed host must not
/// decide its own agent definition, and must not hold the write role that
/// provisioning requires.
/// </para>
/// </remarks>
internal sealed class FoundryAgentOptionsValidator : IValidateOptions<FoundryAgentOptions>
{
    private readonly IHostEnvironment _environment;

    /// <summary>Initialises the validator.</summary>
    public FoundryAgentOptionsValidator(IHostEnvironment environment) =>
        _environment = environment;

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, FoundryAgentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.IsVersionPinned || _environment.IsDevelopment())
        {
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail(
            $"Azure:AiFoundry:Agent:Version must be set outside Development (this host is '{_environment.EnvironmentName}'). " +
            "Pin the agent version this deployment answers with; run once in Development to provision one, " +
            "and the version it settles on is logged.");
    }
}
