using Azure.Core;
using KnowledgeAssistant.Application.Interfaces;
using KnowledgeAssistant.Infrastructure.Azure.Blob;
using KnowledgeAssistant.Infrastructure.Azure.Common;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KnowledgeAssistant.Infrastructure.DependencyInjection;

/// <summary>
/// The Infrastructure layer's composition root.
/// </summary>
/// <remarks>
/// The single entry point the API calls. <c>Program.cs</c> knows this method and
/// nothing else about this project — no concrete adapter type is named outside
/// this file, so implementations can be renamed, split, or replaced without
/// touching the host.
/// </remarks>
public static class InfrastructureServiceRegistration
{
    /// <summary>
    /// Registers the Azure clients and the adapters that satisfy the
    /// Application layer's ports.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddAzureCredential(configuration);
        services.AddBlobStorage(configuration);

        return services;
    }

    /// <summary>
    /// Binds the credential settings shared by every Azure client.
    /// </summary>
    /// <remarks>
    /// Registered before the adapters because each of them reads the resulting
    /// credential. No <c>ValidateDataAnnotations</c> call here: every value in
    /// this section is optional, and the empty case is the normal one.
    /// </remarks>
    private static IServiceCollection AddAzureCredential(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<AzureCredentialOptions>()
            .Bind(configuration.GetSection(AzureCredentialOptions.SectionName));

        return services;
    }

    private static IServiceCollection AddBlobStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ValidateOnStart is the point of this block. Without it, a missing
        // ServiceUri surfaces as a NullReferenceException on the first upload —
        // in production, under load, at the worst possible moment. With it, the
        // process refuses to start and the deployment fails visibly.
        services
            .AddOptions<BlobStorageOptions>()
            .Bind(configuration.GetSection(BlobStorageOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Read once here so the client factory below has an endpoint to build
        // from. The registration above still governs what the running
        // application sees, so validation is not bypassed.
        BlobStorageOptions options =
            configuration.GetSection(BlobStorageOptions.SectionName).Get<BlobStorageOptions>()
            ?? new BlobStorageOptions();

        // Read from Azure:Credential — the section appsettings.json and
        // CONFIGURATION.md have always documented. Read directly rather than
        // resolved from DI because UseCredential runs during registration, before
        // any service provider exists to resolve IOptions from.
        AzureCredentialOptions credentialOptions =
            configuration.GetSection(AzureCredentialOptions.SectionName).Get<AzureCredentialOptions>()
            ?? new AzureCredentialOptions();

        services.AddAzureClients(clientBuilder =>
        {
            clientBuilder
                .AddBlobServiceClient(ResolveServiceUri(options))
                .ConfigureOptions(clientOptions =>
                {
                    // The storage SDK defaults to 5 retries with a long backoff,
                    // which is tuned for background work. On a synchronous upload
                    // it means a caller waits the better part of a minute to be
                    // told storage is down — measured at 52 seconds against an
                    // unreachable account. A user-facing request should fail while
                    // someone is still waiting for it.
                    clientOptions.Retry.MaxRetries = 3;
                    clientOptions.Retry.Mode = RetryMode.Exponential;
                    clientOptions.Retry.Delay = TimeSpan.FromMilliseconds(200);
                    clientOptions.Retry.MaxDelay = TimeSpan.FromSeconds(2);

                    // Generous enough for a 20 MB body on a slow connection, and
                    // still bounded so a hung socket cannot hold a request thread
                    // indefinitely.
                    clientOptions.Retry.NetworkTimeout = TimeSpan.FromSeconds(60);
                });

            // One credential for every Azure client registered here. The chain
            // resolves to the developer's own identity locally (Azure CLI,
            // Visual Studio) and to the managed identity in Azure — so the same
            // code path runs in both, and no key ever exists to be leaked.
            clientBuilder.UseCredential(AzureCredentialFactory.Create(credentialOptions));
        });

        // Singleton: see BlobStorageService's remarks. The container-existence
        // cache and the pooled BlobServiceClient both depend on this lifetime.
        services.AddSingleton<IBlobStorageService, BlobStorageService>();

        return services;
    }

    /// <summary>
    /// Produces the endpoint for the client factory.
    /// </summary>
    /// <remarks>
    /// A placeholder is substituted when configuration is absent so that
    /// registration itself cannot throw. Startup still fails — but through
    /// <c>ValidateOnStart</c>, which reports precisely which setting is missing,
    /// rather than through a <see cref="UriFormatException"/> raised deep inside
    /// service registration with no indication of the cause.
    /// </remarks>
    private static Uri ResolveServiceUri(BlobStorageOptions options) =>
        Uri.TryCreate(options.ServiceUri, UriKind.Absolute, out Uri? serviceUri)
            ? serviceUri
            : new Uri("https://unconfigured.invalid/");
}
