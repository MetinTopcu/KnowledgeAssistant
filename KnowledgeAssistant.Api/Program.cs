using KnowledgeAssistant.Api.Extensions;
using KnowledgeAssistant.Api.Observability;
using KnowledgeAssistant.Application.DependencyInjection;
using KnowledgeAssistant.Infrastructure.DependencyInjection;
using KnowledgeAssistant.Infrastructure.Diagnostics;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// The default providers are cleared so that Serilog's console sink is the only
// thing writing to stdout. Without this, AddSerilog(writeToProviders: true)
// below would leave the framework's own console provider active alongside
// Serilog's sink and every line would appear twice.
builder.Logging.ClearProviders();

// Reads the "Serilog" section already present in appsettings.json, so logging is
// configured by deployment rather than by code.
//
// writeToProviders: true is what lets log records reach the OpenTelemetry
// provider registered by AddObservability, and through it Application Insights.
// Left at its default of false, Serilog consumes the pipeline: the console would
// still be perfect and not one log line would reach the telemetry backend.
builder.Services.AddSerilog(
    (services, configuration) => configuration
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.With<ActivityEnricher>(),
    writeToProviders: true);

// Registered before the layers below so that the tracer and meter providers
// exist before anything they observe is constructed.
builder.AddObservability();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddInfrastructureHealthChecks(builder.Configuration);
builder.Services.AddApiServices();
builder.Services.AddHealthEndpoints();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// First in the pipeline, so every subsequent component — including the request
// logger below — sees the correlation identifier it establishes.
app.UseMiddleware<CorrelationIdMiddleware>();

// Replaces the framework's per-request log noise with one enriched completion
// line carrying method, path, status, and elapsed time.
app.UseSerilogRequestLogging(options =>
{
    // Health probes are logged at Verbose, which the configured minimum level
    // discards. They are polled every few seconds forever, and at Information
    // they would bury every real request in the log. The status code still
    // reaches the metrics pipeline, so a failing probe remains visible where it
    // matters.
    options.GetLevel = (httpContext, _, exception) =>
        exception is not null
            ? Serilog.Events.LogEventLevel.Error
            : httpContext.Request.Path.StartsWithSegments("/health")
                ? Serilog.Events.LogEventLevel.Verbose
                : Serilog.Events.LogEventLevel.Information;

    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("Host", httpContext.Request.Host.Value);
        diagnosticContext.Set("Protocol", httpContext.Request.Protocol);
        diagnosticContext.Set("ContentLength", httpContext.Response.ContentLength);

        // The matched route rather than the raw path: a path carries ids, which
        // makes every request a unique string and defeats grouping. Deliberately
        // no client IP or user agent — neither is needed to operate this service,
        // and both are personal data once written to a log that is retained.
        diagnosticContext.Set("Endpoint", httpContext.GetEndpoint()?.DisplayName);
    };
});

app.UseHttpsRedirection();

app.MapControllers();

app.MapHealthEndpoints();

await app.RunAsync().ConfigureAwait(false);

// Exposed so an integration-test project can drive this host through
// WebApplicationFactory<Program>, which requires a nameable entry-point type.
/// <summary>The application entry point.</summary>
public partial class Program;
