using Azure.Monitor.OpenTelemetry.AspNetCore;
using IC.Agentic.Orchestration.Telemetry;
using IC.BillerExperience.Worker;
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
builder.Services.AddHostedService<PublicationWorker>();
builder.Services.AddHealthChecks();
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
