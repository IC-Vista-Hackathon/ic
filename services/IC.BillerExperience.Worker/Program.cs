using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Azure.Storage.Blobs;
using IC.Agentic.Orchestration.Telemetry;
using IC.BillerExperience.Worker;
using IC.BillerExperience.Worker.Artifacts;
using IC.BillerExperience.Worker.Persistence;
using Microsoft.Azure.Cosmos;
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
    new CosmosClientOptions { ApplicationName = "IC.BillerExperience.Worker" }));
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
var openTelemetry = builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("IC.BillerExperience.Worker"))
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
