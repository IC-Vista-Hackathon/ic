using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Azure.Storage.Blobs;
using Pronto.Agentic.Orchestration.Telemetry;
using Pronto.BillerExperience.Worker;
using Pronto.BillerExperience.Worker.Artifacts;
using Pronto.BillerExperience.Worker.Persistence;
using Microsoft.Azure.Cosmos;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(console =>
{
    console.IncludeScopes = true;
    console.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
    console.UseUtcTimestamp = true;
});
var publicationOptions = builder.Configuration
    .GetSection(PublicationOptions.SectionName)
    .Get<PublicationOptions>() ?? new PublicationOptions();
if (string.IsNullOrWhiteSpace(publicationOptions.CosmosEndpoint))
{
    throw new InvalidOperationException("Publication:CosmosEndpoint is required.");
}
if (string.IsNullOrWhiteSpace(publicationOptions.StorageEndpoint))
{
    throw new InvalidOperationException("Publication:StorageEndpoint is required.");
}

builder.Services.Configure<PublicationOptions>(builder.Configuration.GetSection(PublicationOptions.SectionName));
builder.Services.AddSingleton(new CosmosClient(
    publicationOptions.CosmosEndpoint,
    new DefaultAzureCredential(),
    new CosmosClientOptions { ApplicationName = "Pronto.BillerExperience.Worker" }));
builder.Services.AddSingleton<IPublicationRepository>(services => new CosmosPublicationRepository(
    services.GetRequiredService<CosmosClient>(),
    publicationOptions.DatabaseName,
    services.GetRequiredService<ILogger<CosmosPublicationRepository>>()));
builder.Services.AddSingleton(new BlobServiceClient(
    new Uri(publicationOptions.StorageEndpoint),
    new DefaultAzureCredential()));
builder.Services.AddSingleton(services => services.GetRequiredService<BlobServiceClient>()
    .GetBlobContainerClient(publicationOptions.ContainerName));
builder.Services.AddSingleton<PublicationArtifactPlanFactory>();
builder.Services.AddSingleton<IExperienceArtifactPublisher, BlobExperienceArtifactPublisher>();
builder.Services.AddSingleton<PublicationProcessor>();
builder.Services.AddHostedService<PublicationWorker>();
builder.Services.AddHealthChecks().AddCheck<PublicationHealthCheck>("publication_dependencies");
// Health probes (/health/live, /health/ready) are polled continuously; exclude them from
// ASP.NET Core trace collection so they don't flood Application Insights, while normal requests
// are still exported. No-op unless the AspNetCore instrumentation is active (via Azure Monitor).
builder.Services.Configure<AspNetCoreTraceInstrumentationOptions>(instrumentation =>
    instrumentation.Filter = context =>
        !(context.Request.Path.Equals("/health/live", StringComparison.OrdinalIgnoreCase)
            || context.Request.Path.Equals("/health/ready", StringComparison.OrdinalIgnoreCase)));
var openTelemetry = builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("Pronto.BillerExperience.Worker"))
    .WithTracing(tracing => tracing
        .AddSource(OrchestrationTelemetry.ActivitySourceName)
        .AddSource(PublicationTelemetry.SourceName))
    .WithMetrics(metrics => metrics
        .AddMeter(OrchestrationTelemetry.MeterName)
        .AddMeter(PublicationTelemetry.MeterName));
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    openTelemetry.UseAzureMonitor();
}

var app = builder.Build();

app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

await app.RunAsync();

// Host bootstrap - configuration/DI wiring, only executable against live Azure.
// Excluded from coverage metrics; behavior is exercised by the functional tests.
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public partial class Program
{
}
