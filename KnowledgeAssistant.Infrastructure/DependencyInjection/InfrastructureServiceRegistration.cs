using System.ClientModel.Primitives;
using Azure.AI.DocumentIntelligence;
using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Core.Extensions;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Storage.Blobs;
using Azure.Search.Documents.Indexes;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects.Agents;
using KnowledgeAssistant.Application.Interfaces;
using KnowledgeAssistant.Infrastructure.Azure.Agents;
using KnowledgeAssistant.Infrastructure.Azure.Blob;
using KnowledgeAssistant.Infrastructure.Azure.Common;
using KnowledgeAssistant.Infrastructure.Azure.DocumentIntelligence;
using KnowledgeAssistant.Infrastructure.Azure.OpenAI;
using KnowledgeAssistant.Infrastructure.Search;
using KnowledgeAssistant.Infrastructure.Search.Chunking;
using KnowledgeAssistant.Infrastructure.Search.Vectors;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
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

        // Built once and shared by every client registration below. A second
        // instance would mean a second token cache and a duplicate round trip to
        // Entra ID for a token the first one already holds.
        AzureCredentialOptions credentialOptions =
            configuration.GetSection(AzureCredentialOptions.SectionName).Get<AzureCredentialOptions>()
            ?? new AzureCredentialOptions();

        DefaultAzureCredential credential = AzureCredentialFactory.Create(credentialOptions);

        services.AddBlobStorage(configuration, credential);
        services.AddAzureSearch(configuration, credential);
        services.AddDocumentChunking(configuration, credential);
        services.AddEmbeddings(configuration, credential);
        services.AddFoundryAgent(configuration, credential);

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
        IConfiguration configuration,
        DefaultAzureCredential credential)
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

        // ServiceUri is required only when no Azurite connection string is set,
        // and the connection string is allowed only in Development. Neither rule
        // fits an attribute, so both live in the validator. ValidateOnStart above
        // runs it too.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<BlobStorageOptions>, BlobStorageOptionsValidator>());

        // Read once here so the client factory below has an endpoint to build
        // from. The registration above still governs what the running
        // application sees, so validation is not bypassed.
        BlobStorageOptions options =
            configuration.GetSection(BlobStorageOptions.SectionName).Get<BlobStorageOptions>()
            ?? new BlobStorageOptions();

        services.AddAzureClients(clientBuilder =>
        {
            // Two ways to build the same client, with the same retry settings.
            // The ServiceUri path is the production one: it authenticates with the
            // shared credential below. The connection-string path exists only for
            // Azurite, which accepts shared-key auth over plain HTTP and nothing
            // else. The validator refuses it outside Development, so a deployed
            // host that reaches this branch fails at startup. That client ignores
            // UseCredential, and every other client still uses it.
            IAzureClientBuilder<BlobServiceClient, BlobClientOptions> blobClient = options.UsesDevelopmentStorage
                ? clientBuilder.AddBlobServiceClient(options.ConnectionString)
                : clientBuilder.AddBlobServiceClient(ResolveServiceUri(options));

            blobClient
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
            clientBuilder.UseCredential(credential);
        });

        // Singleton: see BlobStorageService's remarks. The container-existence
        // cache and the pooled BlobServiceClient both depend on this lifetime.
        services.AddSingleton<IBlobStorageService, BlobStorageService>();

        return services;
    }

    /// <summary>
    /// Registers the Azure AI Search client and the indexing adapter.
    /// </summary>
    private static IServiceCollection AddAzureSearch(
        this IServiceCollection services,
        IConfiguration configuration,
        DefaultAzureCredential credential)
    {
        // Same contract as blob storage: a missing endpoint fails the deployment
        // at startup with a message naming the setting, rather than surfacing as
        // an obscure failure on the first upload that reaches indexing.
        services
            .AddOptions<AzureSearchOptions>()
            .Bind(configuration.GetSection(AzureSearchOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        AzureSearchOptions options =
            configuration.GetSection(AzureSearchOptions.SectionName).Get<AzureSearchOptions>()
            ?? new AzureSearchOptions();

        services.AddAzureClients(clientBuilder =>
        {
            // The generic AddClient overload: Microsoft.Extensions.Azure ships
            // first-class extensions for Blob and Key Vault but none for Search,
            // so the client is constructed here. Taking the credential from the
            // factory's own parameter — rather than closing over one — is what
            // guarantees this client uses the same credential as every other.
            clientBuilder.AddClient<SearchIndexClient, SearchClientOptions>(
                (clientOptions, factoryCredential) =>
                {
                    // Matches the blob tuning and for the same reason: the SDK
                    // default of 5 retries with a long backoff is tuned for
                    // background work, and would leave a caller waiting most of a
                    // minute to learn that search is down. A user-facing request
                    // should fail while someone is still waiting for it.
                    clientOptions.Retry.MaxRetries = 3;
                    clientOptions.Retry.Mode = RetryMode.Exponential;
                    clientOptions.Retry.Delay = TimeSpan.FromMilliseconds(200);
                    clientOptions.Retry.MaxDelay = TimeSpan.FromSeconds(2);
                    clientOptions.Retry.NetworkTimeout = TimeSpan.FromSeconds(30);

                    return new SearchIndexClient(
                        ResolveEndpoint(options.Endpoint),
                        factoryCredential,
                        clientOptions);
                });

            // Repeated deliberately. UseCredential applies to the builder it is
            // called on, and this is a separate AddAzureClients block from the
            // blob one; relying on the two to share state would make the search
            // client's authentication depend on registration order. The credential
            // instance itself is shared, so this costs nothing.
            clientBuilder.UseCredential(credential);
        });

        // Singleton for the same reasons as the blob adapter: the SDK clients are
        // thread-safe and expensive to construct, and the index-existence cache
        // depends on this lifetime.
        services.AddSingleton<IAzureSearchService, AzureSearchService>();

        // The vector adapter shares the SearchIndexClient registered above — one
        // pipeline, one credential, one token cache — and derives its own
        // SearchClient for the chunk index from it. It is a separate service
        // because it writes a separate index with a different key.
        services.AddSingleton<IVectorIndexService, AzureSearchVectorIndexService>();

        return services;
    }

    /// <summary>
    /// Registers the chunking service and the text extractor behind it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The extractor is chosen here, once, rather than per call.</b> Whether
    /// Document Intelligence is available is a fact about the deployment, not
    /// about any individual document, so resolving it at registration means the
    /// running system has exactly one extraction path and the container states
    /// which one it is.
    /// </para>
    /// <para>
    /// <b>There is deliberately no runtime fallback.</b> Silently dropping to
    /// local extraction when a configured endpoint misbehaves would fill one
    /// corpus with documents processed by two engines of different capability —
    /// scans readable or unreadable depending on the weather — with nothing
    /// recording which produced what. A configured endpoint that fails is an
    /// incident, and it is reported as one.
    /// </para>
    /// </remarks>
    private static IServiceCollection AddDocumentChunking(
        this IServiceCollection services,
        IConfiguration configuration,
        DefaultAzureCredential credential)
    {
        services
            .AddOptions<ChunkingOptions>()
            .Bind(configuration.GetSection(ChunkingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddOptions<DocumentIntelligenceOptions>()
            .Bind(configuration.GetSection(DocumentIntelligenceOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        DocumentIntelligenceOptions options =
            configuration.GetSection(DocumentIntelligenceOptions.SectionName).Get<DocumentIntelligenceOptions>()
            ?? new DocumentIntelligenceOptions();

        if (options.IsConfigured)
        {
            services.AddAzureClients(clientBuilder =>
            {
                clientBuilder.AddClient<DocumentIntelligenceClient, DocumentIntelligenceClientOptions>(
                    (clientOptions, factoryCredential) =>
                    {
                        // Analysis is a long-running operation the SDK polls, so a
                        // generous network timeout here is about each individual
                        // poll, not about the overall wait. The retry budget stays
                        // in line with the other adapters.
                        clientOptions.Retry.MaxRetries = 3;
                        clientOptions.Retry.Mode = RetryMode.Exponential;
                        clientOptions.Retry.Delay = TimeSpan.FromMilliseconds(500);
                        clientOptions.Retry.MaxDelay = TimeSpan.FromSeconds(5);
                        clientOptions.Retry.NetworkTimeout = TimeSpan.FromSeconds(60);

                        return new DocumentIntelligenceClient(
                            ResolveEndpoint(options.Endpoint),
                            factoryCredential,
                            clientOptions);
                    });

                clientBuilder.UseCredential(credential);
            });

            services.AddSingleton<IPdfTextExtractor, DocumentIntelligenceTextExtractor>();
        }
        else
        {
            services.AddSingleton<IPdfTextExtractor, PdfPigTextExtractor>();
        }

        services.AddSingleton<IDocumentChunkingService, DocumentChunkingService>();

        return services;
    }

    /// <summary>
    /// Registers the Azure OpenAI client and the embedding adapter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Registered by hand rather than through <c>AddAzureClients</c>.</b>
    /// <see cref="AzureOpenAIClient"/> is built on System.ClientModel, not
    /// Azure.Core: its options derive from <see cref="ClientPipelineOptions"/>
    /// rather than <c>Azure.Core.ClientOptions</c>, so the
    /// <c>Microsoft.Extensions.Azure</c> factory cannot construct it. Two
    /// singletons are all that is needed, and the shared credential is passed
    /// explicitly, so the outcome is the same.
    /// </para>
    /// <para>
    /// <b>The SDK's own retry is switched off here</b>, and this is the reason to
    /// read this method carefully. Every other adapter in this solution sets
    /// <c>Retry.MaxRetries = 3</c> and relies on it. This one delegates retry to
    /// Polly instead, and the two must not both be active: retry layers compose
    /// multiplicatively, so four Polly attempts over three SDK retries is twelve
    /// requests — sent, most likely, to a deployment that is already rejecting
    /// calls for receiving too many. One layer owns the decision, and it is the
    /// one that can read <c>Retry-After</c>.
    /// </para>
    /// </remarks>
    private static IServiceCollection AddEmbeddings(
        this IServiceCollection services,
        IConfiguration configuration,
        DefaultAzureCredential credential)
    {
        services
            .AddOptions<AzureOpenAIOptions>()
            .Bind(configuration.GetSection(AzureOpenAIOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        AzureOpenAIOptions options =
            configuration.GetSection(AzureOpenAIOptions.SectionName).Get<AzureOpenAIOptions>()
            ?? new AzureOpenAIOptions();

        services.AddSingleton(_ =>
        {
            var clientOptions = new AzureOpenAIClientOptions
            {
                // Polly owns retry. See the remarks above.
                RetryPolicy = new ClientRetryPolicy(maxRetries: 0),

                // Bounds a single attempt. The overall wait is governed by the
                // resilience pipeline, which is where the retry budget lives.
                NetworkTimeout = TimeSpan.FromSeconds(100),
            };

            return new AzureOpenAIClient(ResolveEndpoint(options.Endpoint), credential, clientOptions);
        });

        // Derived from the parent client so both share one pipeline, one
        // credential, and one token cache. The deployment name is bound here so
        // that no other type needs to know it.
        services.AddSingleton(provider =>
            provider.GetRequiredService<AzureOpenAIClient>()
                .GetEmbeddingClient(options.EmbeddingDeploymentName));

        services.AddSingleton<IEmbeddingService, AzureOpenAIEmbeddingService>();

        // A second client from the same parent, so chat and embedding share one
        // pipeline, one credential, and one token cache. The deployment name is
        // bound here so no other type needs to know it.
        services.AddSingleton(provider =>
            provider.GetRequiredService<AzureOpenAIClient>()
                .GetChatClient(options.ChatDeploymentName));

        services.AddSingleton<IChatService, AzureOpenAIChatService>();

        return services;
    }

    /// <summary>
    /// Registers the Azure AI Foundry agent, its tool, and the clients behind them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Everything is built by hand here, as it is for embeddings, and for the
    /// same reason.</b> The Foundry clients are System.ClientModel types: their
    /// options derive from <see cref="ClientPipelineOptions"/> rather than
    /// <c>Azure.Core.ClientOptions</c>, so the <c>Microsoft.Extensions.Azure</c>
    /// factory cannot construct them. The shared credential is passed explicitly,
    /// so the outcome is identical — one identity for every client in the process.
    /// </para>
    /// <para>
    /// <b>The SDK's own retry is switched off on both clients</b>, exactly as it is
    /// for the OpenAI client. Retry layers compose multiplicatively, so four Polly
    /// attempts over three SDK retries is twelve requests sent to a service that is
    /// most likely failing because it is already receiving too many. One layer owns
    /// the decision, and it is the one that can read <c>Retry-After</c>.
    /// </para>
    /// <para>
    /// <b>The retry budget is the OpenAI adapter's, deliberately.</b> The agent runs
    /// on the same AI Foundry resource as chat and embeddings and is throttled by
    /// the same quota, so <c>Azure:AiFoundry</c>'s retry settings govern all three.
    /// A separate budget for the agent would be a second thing to tune and a second
    /// thing to forget.
    /// </para>
    /// <para>
    /// <b>Why the responses client is a factory rather than a registration.</b> It
    /// is constructed against a specific agent version, and which version that is
    /// can only be known after the first call resolves it. The delegate keeps the
    /// endpoint, the credential, and the pipeline options chosen here with every
    /// other client, and lets the adapter supply the one value it discovers.
    /// </para>
    /// </remarks>
    private static IServiceCollection AddFoundryAgent(
        this IServiceCollection services,
        IConfiguration configuration,
        DefaultAzureCredential credential)
    {
        services
            .AddOptions<FoundryAgentOptions>()
            .Bind(configuration.GetSection(FoundryAgentOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        FoundryAgentOptions agentOptions =
            configuration.GetSection(FoundryAgentOptions.SectionName).Get<FoundryAgentOptions>()
            ?? new FoundryAgentOptions();

        AzureOpenAIOptions openAIOptions =
            configuration.GetSection(AzureOpenAIOptions.SectionName).Get<AzureOpenAIOptions>()
            ?? new AzureOpenAIOptions();

        // Both fall back to the parent AI Foundry settings when unset, so a
        // single-project account needs no agent configuration at all. Resolved
        // once, here, rather than at each use: whether a deployment separates its
        // agent model from its chat model is a fact about the deployment, not
        // about any individual question.
        Uri projectEndpoint = ResolveEndpoint(
            string.IsNullOrWhiteSpace(agentOptions.ProjectEndpoint)
                ? openAIOptions.Endpoint
                : agentOptions.ProjectEndpoint);

        string modelDeploymentName = string.IsNullOrWhiteSpace(agentOptions.ModelDeploymentName)
            ? openAIOptions.ChatDeploymentName
            : agentOptions.ModelDeploymentName;

        services.AddSingleton(provider =>
        {
            var clientOptions = new AgentAdministrationClientOptions
            {
                // Polly owns retry. See the remarks above.
                RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
                NetworkTimeout = TimeSpan.FromSeconds(30),
            };

            return new FoundryAgentProvisioner(
                new AgentAdministrationClient(projectEndpoint, credential, clientOptions),
                agentOptions,
                modelDeploymentName,
                OpenAIResiliencePipeline.Create(
                    openAIOptions,
                    provider.GetRequiredService<ILogger<FoundryAgentProvisioner>>(),
                    "agent provisioning"),
                provider.GetRequiredService<ILogger<FoundryAgentProvisioner>>());
        });

        services.AddSingleton<FoundryResponsesClientFactory>(_ => agent =>
        {
            var clientOptions = new ProjectResponsesClientOptions
            {
                RetryPolicy = new ClientRetryPolicy(maxRetries: 0),

                // Bounds a single attempt, and is longer than the other clients'
                // because one turn of an agent run includes the model's reasoning
                // rather than only a completion. The overall wait is still governed
                // by the resilience pipeline.
                NetworkTimeout = TimeSpan.FromSeconds(120),
            };

            return new ProjectResponsesClient(
                projectEndpoint,
                credential,
                agent,

                // No default conversation. Every question is answered on its own,
                // which is the statelessness IAgentService promises; binding a
                // conversation here would quietly turn the service multi-turn and
                // start accumulating whatever people asked.
                defaultConversationId: null,
                clientOptions);
        });

        // The tool retrieves through the same two ports the retrieval slice uses,
        // which is what makes "the agent's knowledge source is the existing RAG
        // pipeline" a fact about the object graph rather than a claim.
        services.AddSingleton(provider => new KnowledgeSearchTool(
            provider.GetRequiredService<IEmbeddingService>(),
            provider.GetRequiredService<IAzureSearchService>(),
            agentOptions,
            provider.GetRequiredService<ILogger<KnowledgeSearchTool>>()));

        // Singleton: the resolved agent version, the responses client, and the
        // retry state all live for the process. See the adapter's remarks.
        services.AddSingleton<IAgentService>(provider => new AzureAIFoundryAgentService(
            provider.GetRequiredService<FoundryResponsesClientFactory>(),
            provider.GetRequiredService<FoundryAgentProvisioner>(),
            provider.GetRequiredService<KnowledgeSearchTool>(),
            agentOptions,
            OpenAIResiliencePipeline.Create(
                openAIOptions,
                provider.GetRequiredService<ILogger<AzureAIFoundryAgentService>>(),
                "agent"),
            provider.GetRequiredService<ILogger<AzureAIFoundryAgentService>>()));

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
        ResolveEndpoint(options.ServiceUri);

    private static Uri ResolveEndpoint(string configuredEndpoint) =>
        Uri.TryCreate(configuredEndpoint, UriKind.Absolute, out Uri? endpoint)
            ? endpoint
            : new Uri("https://unconfigured.invalid/");
}
