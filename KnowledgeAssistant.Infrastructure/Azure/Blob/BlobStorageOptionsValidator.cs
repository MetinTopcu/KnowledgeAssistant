using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace KnowledgeAssistant.Infrastructure.Azure.Blob;

/// <summary>
/// Decides which of the two ways to reach blob storage a configuration selects,
/// and refuses the local one anywhere it does not belong.
/// </summary>
/// <remarks>
/// <para>
/// <b>Production rules are unchanged.</b> With no connection string,
/// <see cref="BlobStorageOptions.ServiceUri"/> is required and must be an
/// absolute URL, reported with the same messages the <c>[Required]</c> and
/// <c>[Url]</c> attributes produced before the emulator path existed. The
/// attributes had to move here only because the rule became conditional.
/// </para>
/// <para>
/// <b>Why an <see cref="IValidateOptions{TOptions}"/> rather than
/// <c>IValidatableObject</c>.</b> Two reasons. The environment check needs
/// <see cref="IHostEnvironment"/>, which a data-annotations context cannot
/// supply. And the options framework runs this alongside the attribute checks
/// rather than after them, so a deployment missing both the endpoint and the
/// container is still told about both at once.
/// </para>
/// <para>
/// <b>The emulator path is fenced twice.</b> The environment fence keeps a
/// connection string out of any deployed host, even one misconfigured by a stray
/// environment variable. The account fence means that even in Development the
/// only key this path can ever carry is Azurite's, which Microsoft publishes and
/// which no real storage account has — so it is not a secret, and a real key
/// pasted here fails startup instead of quietly working.
/// </para>
/// </remarks>
internal sealed class BlobStorageOptionsValidator : IValidateOptions<BlobStorageOptions>
{
    /// <summary>The account name Azurite serves.</summary>
    internal const string AzuriteAccountName = "devstoreaccount1";

    /// <summary>
    /// Azurite's well-known account key, as published in Microsoft's Azurite
    /// documentation. Not a secret: it is identical on every installation.
    /// </summary>
    internal const string AzuriteAccountKey =
        "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

    // Reused rather than reimplemented so the production URL rule is the exact
    // one the attribute enforced, not a close approximation of it.
    private static readonly UrlAttribute UrlRule = new();

    private readonly IHostEnvironment _environment;

    /// <summary>Initialises the validator.</summary>
    public BlobStorageOptionsValidator(IHostEnvironment environment) =>
        _environment = environment;

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, BlobStorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.UsesDevelopmentStorage)
        {
            if (string.IsNullOrWhiteSpace(options.ServiceUri))
            {
                return ValidateOptionsResult.Fail("Azure:Storage:ServiceUri must be configured.");
            }

            return UrlRule.IsValid(options.ServiceUri)
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail("Azure:Storage:ServiceUri must be an absolute URL.");
        }

        var failures = new List<string>();

        if (!_environment.IsDevelopment())
        {
            failures.Add(
                $"Azure:Storage:ConnectionString is only permitted in the Development environment, and this host is running as '{_environment.EnvironmentName}'. " +
                "Deployed environments must use Azure:Storage:ServiceUri, which authenticates with DefaultAzureCredential.");
        }

        if (!string.IsNullOrWhiteSpace(options.ServiceUri))
        {
            failures.Add(
                "Azure:Storage:ServiceUri and Azure:Storage:ConnectionString are both set. Set exactly one: " +
                "ServiceUri for Azure, ConnectionString for the local Azurite emulator.");
        }

        if (!IsAzuriteConnectionString(options.ConnectionString))
        {
            failures.Add(
                "Azure:Storage:ConnectionString must target the Azurite emulator: either 'UseDevelopmentStorage=true', " +
                $"or AccountName={AzuriteAccountName} with Azurite's published AccountKey and an explicit BlobEndpoint.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>
    /// Whether <paramref name="connectionString"/> can only ever reach Azurite.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Parsed to match what <c>Azure.Storage.Blobs</c> 12.29.1 actually accepts,
    /// verified against that package rather than assumed: setting names and the
    /// <c>true</c> value are case-insensitive, and whitespace around <c>=</c> is
    /// rejected by the SDK, so it is not trimmed here either.
    /// </para>
    /// <para>
    /// <b>BlobEndpoint is required in the explicit form.</b> Without it the SDK
    /// resolves <c>devstoreaccount1</c> to
    /// <c>https://devstoreaccount1.blob.core.windows.net/</c> — a public Azure host
    /// — and would send the emulator key there.
    /// </para>
    /// <para>
    /// A <c>SharedAccessSignature</c> is rejected outright: unlike Azurite's key, a
    /// SAS token is a real, issued credential.
    /// </para>
    /// </remarks>
    internal static bool IsAzuriteConnectionString(string connectionString)
    {
        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string segment in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = segment.IndexOf('=', StringComparison.Ordinal);

            // A duplicated setting is rejected rather than resolved: which copy
            // the SDK would honour is not something this check should guess.
            if (separator <= 0 || !settings.TryAdd(segment[..separator], segment[(separator + 1)..]))
            {
                return false;
            }
        }

        if (settings.ContainsKey("SharedAccessSignature"))
        {
            return false;
        }

        if (settings.TryGetValue("UseDevelopmentStorage", out string? useDevelopmentStorage))
        {
            return settings.Count == 1
                && string.Equals(useDevelopmentStorage, "true", StringComparison.OrdinalIgnoreCase);
        }

        return settings.TryGetValue("AccountName", out string? accountName)
            && string.Equals(accountName, AzuriteAccountName, StringComparison.Ordinal)
            && settings.TryGetValue("AccountKey", out string? accountKey)
            && string.Equals(accountKey, AzuriteAccountKey, StringComparison.Ordinal)
            && settings.TryGetValue("BlobEndpoint", out string? blobEndpoint)
            && Uri.TryCreate(blobEndpoint, UriKind.Absolute, out _);
    }
}
