using System.Reflection;
using Azure.Core;
using Azure.Monitor.OpenTelemetry.Exporter;
using KnowledgeAssistant.Application.Diagnostics;
using KnowledgeAssistant.Infrastructure.Azure.Common;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace KnowledgeAssistant.Api.Observability;

/// <summary>
/// Wires collection and export for traces, metrics, and logs.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the only file in the solution that names a telemetry vendor.</b>
/// Application emits through <see cref="System.Diagnostics.ActivitySource"/> and
/// <see cref="System.Diagnostics.Metrics.Meter"/>, which are BCL types;
/// Infrastructure emits nothing beyond what the Azure SDKs already do. The
/// decision about who listens and where it is sent belongs to the composition
/// root, and keeping it here is what makes the backend replaceable without
/// touching a single line of business code.
/// </para>
/// <para>
/// <b>Why the OpenTelemetry exporter rather than the classic Application
/// Insights SDK.</b> The classic SDK runs its own collection pipeline alongside
/// OpenTelemetry's, so a service with both reports every request twice — once as
/// an OTel span and once as an AI request telemetry item — and the two disagree
/// about sampling. The exporter reuses the collection configured here and only
/// changes where it is sent.
/// </para>
/// </remarks>
internal static class ObservabilityRegistration
{
    /// <summary>Configures collection and export from the bound options.</summary>
    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services
            .AddOptions<ObservabilityOptions>()
            .Bind(builder.Configuration.GetSection(ObservabilityOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Read once, here, because the provider graph below is built at
        // registration time. The bound registration above still governs anything
        // that resolves IOptions later, so validation is not bypassed.
        ObservabilityOptions options =
            builder.Configuration.GetSection(ObservabilityOptions.SectionName).Get<ObservabilityOptions>()
            ?? new ObservabilityOptions();

        TokenCredential? credential = ResolveCredential(builder, options);

        builder.Services
            .AddOpenTelemetry()
            .ConfigureResource(resource => ConfigureResource(resource, options))
            .WithTracing(tracing => ConfigureTracing(tracing, options, credential))
            .WithMetrics(metrics => ConfigureMetrics(metrics, options, credential));

        ConfigureLogging(builder, options, credential);

        return builder;
    }

    /// <summary>
    /// Builds the credential the exporter authenticates with, reusing the
    /// Infrastructure factory.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A second credential instance exists in the process: this
    /// one and the one the data-plane clients share. That is deliberate rather
    /// than an oversight. Tokens are cached per resource, and telemetry export
    /// targets <c>monitor.azure.com</c> while storage and search target their own
    /// audiences — so there is no cached token the two could have shared. What is
    /// duplicated is one credential object and one chain probe at startup.
    /// </para>
    /// <para>
    /// The factory is reused so that a deployment naming a user-assigned managed
    /// identity gets the same identity for telemetry as for data. Rebuilding the
    /// policy here by hand is how a service ends up writing documents happily
    /// while silently failing to export a single trace.
    /// </para>
    /// </remarks>
    private static TokenCredential? ResolveCredential(
        WebApplicationBuilder builder,
        ObservabilityOptions options)
    {
        if (!options.AzureMonitor.IsConfigured)
        {
            return null;
        }

        AzureCredentialOptions credentialOptions =
            builder.Configuration.GetSection(AzureCredentialOptions.SectionName).Get<AzureCredentialOptions>()
            ?? new AzureCredentialOptions();

        return AzureCredentialFactory.Create(credentialOptions, builder.Environment);
    }

    /// <summary>
    /// Sets the attributes every signal carries.
    /// </summary>
    /// <remarks>
    /// <c>service.instance.id</c> is the machine name rather than a fresh GUID:
    /// a GUID changes on every restart, so "which instance is slow" becomes
    /// unanswerable across a deployment. Environment is included because the same
    /// build runs in several, and a chart that silently blends them is worse than
    /// no chart.
    /// </remarks>
    private static void ConfigureResource(ResourceBuilder resource, ObservabilityOptions options)
    {
        string version = string.IsNullOrWhiteSpace(options.ServiceVersion)
            ? Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown"
            : options.ServiceVersion;

        resource
            .AddService(
                serviceName: options.ServiceName,
                serviceVersion: version,
                serviceInstanceId: Environment.MachineName)
            .AddAttributes(
            [
                new KeyValuePair<string, object>(
                    "deployment.environment",
                    Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"),
            ]);
    }

    private static void ConfigureTracing(
        TracerProviderBuilder tracing,
        ObservabilityOptions options,
        TokenCredential? credential)
    {
        if (!options.TracingEnabled)
        {
            return;
        }

        tracing.SetSampler(CreateSampler(options.SamplingRatio));

        // The Application layer's own spans, one per use case.
        tracing.AddSource(ApplicationDiagnostics.ActivitySourceName);

        if (options.Instrumentation.AspNetCore)
        {
            tracing.AddAspNetCoreInstrumentation(instrumentation =>
            {
                // Health probes are excluded from tracing entirely. An
                // orchestrator polls them every few seconds, forever, per
                // instance — left in, they are the overwhelming majority of spans
                // in the backend, they push real traces out of any retention
                // window, and they are billed per ingested record. Metrics still
                // count them, so probe failures remain visible.
                instrumentation.Filter = context =>
                    !context.Request.Path.StartsWithSegments(HealthEndpointRegistration.BasePath);

                // Attaches the exception to the span when a request faults, which
                // is what makes a failed trace self-explanatory instead of merely
                // red.
                instrumentation.RecordException = true;
            });
        }

        if (options.Instrumentation.HttpClient)
        {
            tracing.AddHttpClientInstrumentation();
        }

        if (options.Instrumentation.AzureSdk)
        {
            // The Azure SDKs publish activity sources named Azure.* — one per
            // client library. Subscribing by wildcard means a client added later
            // is traced without this list being revisited. The SDKs suppress their
            // own inner HTTP spans while these are collected, so this does not
            // duplicate HttpClient instrumentation.
            tracing.AddSource("Azure.*");
        }

        if (credential is not null)
        {
            tracing.AddAzureMonitorTraceExporter(exporter =>
            {
                exporter.ConnectionString = options.AzureMonitor.ConnectionString;
                exporter.Credential = credential;
            });
        }

        if (options.Otlp.IsConfigured)
        {
            tracing.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(options.Otlp.Endpoint));
        }
    }

    private static void ConfigureMetrics(
        MeterProviderBuilder metrics,
        ObservabilityOptions options,
        TokenCredential? credential)
    {
        if (!options.MetricsEnabled)
        {
            return;
        }

        // This layer's instruments: use-case duration and every slice's own
        // measurements, all published on one meter.
        metrics.AddMeter(ApplicationDiagnostics.MeterName);

        if (options.Instrumentation.AspNetCore)
        {
            // Supplies http.server.request.duration — the standard, semantic
            // -convention request duration histogram. Deliberately not a bespoke
            // instrument: every dashboard, alert template, and backend chart
            // already understands this one by name.
            metrics.AddAspNetCoreInstrumentation();
        }

        if (options.Instrumentation.HttpClient)
        {
            metrics.AddHttpClientInstrumentation();
        }

        if (options.Instrumentation.Runtime)
        {
            // GC pauses, heap size, thread-pool queue depth, exception rate. The
            // signals that explain a latency rise no request-scoped metric can:
            // when every endpoint slows at once, this is where the answer is.
            metrics.AddRuntimeInstrumentation();
        }

        if (credential is not null)
        {
            metrics.AddAzureMonitorMetricExporter(exporter =>
            {
                exporter.ConnectionString = options.AzureMonitor.ConnectionString;
                exporter.Credential = credential;
            });
        }

        if (options.Otlp.IsConfigured)
        {
            metrics.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(options.Otlp.Endpoint));
        }
    }

    /// <summary>
    /// Adds the OpenTelemetry logging provider alongside Serilog.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Serilog keeps ownership of formatting and of the console sink; this adds a
    /// second provider that ships the same records to the telemetry backend, where
    /// they are correlated automatically with the span that was active when they
    /// were written. That correlation is the entire point: a log line and the
    /// trace it belongs to are only useful together, and joining them by timestamp
    /// afterwards does not work under any real load.
    /// </para>
    /// <para>
    /// It depends on Serilog being registered with <c>writeToProviders: true</c>
    /// in <c>Program.cs</c>. Without that, Serilog consumes the pipeline and
    /// nothing reaches this provider.
    /// </para>
    /// </remarks>
    private static void ConfigureLogging(
        WebApplicationBuilder builder,
        ObservabilityOptions options,
        TokenCredential? credential)
    {
        if (!options.LoggingEnabled)
        {
            return;
        }

        builder.Logging.AddOpenTelemetry(logging =>
        {
            // Both default to false, and both are wanted. Without the formatted
            // message a backend that does not understand structured templates
            // shows the template instead of the message; without scopes, the
            // correlation id pushed by the middleware never leaves the process.
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;

            if (credential is not null)
            {
                logging.AddAzureMonitorLogExporter(exporter =>
                {
                    exporter.ConnectionString = options.AzureMonitor.ConnectionString;
                    exporter.Credential = credential;
                });
            }

            if (options.Otlp.IsConfigured)
            {
                logging.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(options.Otlp.Endpoint));
            }
        });
    }

    /// <summary>
    /// Builds the sampler for the configured ratio.
    /// </summary>
    /// <remarks>
    /// Parent-based in every case. A request arriving with a parent that was
    /// already sampled must be recorded regardless of the local ratio, or a
    /// distributed trace comes back with this service missing from the middle of
    /// it — which is precisely when someone is looking for it.
    /// </remarks>
    private static ParentBasedSampler CreateSampler(double ratio) => ratio >= 1.0
        ? new ParentBasedSampler(new AlwaysOnSampler())
        : new ParentBasedSampler(new TraceIdRatioBasedSampler(ratio));
}
