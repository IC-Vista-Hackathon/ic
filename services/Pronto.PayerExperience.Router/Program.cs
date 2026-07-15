using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpenTelemetry.Resources;
using Pronto.PayerExperience.Router;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(console =>
{
    console.IncludeScopes = true;
    console.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
    console.UseUtcTimestamp = true;
});

var routerOptions = builder.Configuration
    .GetSection(RouterOptions.SectionName)
    .Get<RouterOptions>() ?? new RouterOptions();
if (string.IsNullOrWhiteSpace(routerOptions.StorageEndpoint))
{
    throw new InvalidOperationException("Router:StorageEndpoint is required.");
}

builder.Services.Configure<RouterOptions>(builder.Configuration.GetSection(RouterOptions.SectionName));
builder.Services.AddMemoryCache();
builder.Services.AddSingleton(new BlobServiceClient(
    new Uri(routerOptions.StorageEndpoint),
    new DefaultAzureCredential()));
builder.Services.AddSingleton(services => services.GetRequiredService<BlobServiceClient>()
    .GetBlobContainerClient(routerOptions.ContainerName));
builder.Services.AddSingleton<PayerSiteRouter>();
builder.Services.AddHealthChecks()
    .AddCheck<RouterStorageHealthCheck>("payer_experience_storage", tags: ["ready"]);

var openTelemetry = builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("Pronto.PayerExperience.Router"));
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    openTelemetry.UseAzureMonitor();
}

var app = builder.Build();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready");
app.Map("/pay/{**path}", (HttpContext context, PayerSiteRouter router) => router.HandleAsync(context));

await app.RunAsync();

// Host bootstrap - configuration/DI wiring, only executable against live Azure.
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public partial class Program
{
}
