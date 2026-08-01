using KnowledgeAssistant.Api.Extensions;
using KnowledgeAssistant.Application.DependencyInjection;
using KnowledgeAssistant.Infrastructure.DependencyInjection;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Reads the "Serilog" section already present in appsettings.json, so logging is
// configured by deployment rather than by code.
builder.Services.AddSerilog((services, configuration) => configuration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services));

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApiServices();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Replaces the framework's per-request log noise with one enriched completion
// line carrying method, path, status, and elapsed time.
app.UseSerilogRequestLogging();

app.UseHttpsRedirection();

app.MapControllers();

await app.RunAsync().ConfigureAwait(false);

// Exposed so an integration-test project can drive this host through
// WebApplicationFactory<Program>, which requires a nameable entry-point type.
/// <summary>The application entry point.</summary>
public partial class Program;
