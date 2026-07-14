using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using IC.BillerExperience.Api;
using IC.BillerExperience.Api.Application;
using IC.BillerExperience.Api.Configuration;
using IC.BillerExperience.Api.Infrastructure;
using IC.BillerExperience.Api.Infrastructure.AI;
using IC.BillerExperience.Api.Infrastructure.Persistence;
using IC.Agentic.Orchestration.Abstractions;
using IC.Agentic.Orchestration.Execution;
using IC.Agentic.Orchestration.Telemetry;
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

var options = builder.Configuration
    .GetSection(BillerExperienceOptions.SectionName)
    .Get<BillerExperienceOptions>() ?? new BillerExperienceOptions();

builder.Services.Configure<BillerExperienceOptions>(builder.Configuration.GetSection(BillerExperienceOptions.SectionName));
builder.Services.AddSingleton<IOrchestrationRunner, OrchestrationRunner>();
builder.Services.AddSingleton<BillerOnboardingService>();
builder.Services.AddSingleton<DeterministicExperienceDraftGenerator>();

if (string.Equals(options.Persistence.Provider, "Cosmos", StringComparison.OrdinalIgnoreCase))
{
    if (string.IsNullOrWhiteSpace(options.Persistence.CosmosEndpoint))
    {
        throw new InvalidOperationException("BillerExperience:Persistence:CosmosEndpoint is required for the Cosmos provider.");
    }
    builder.Services.AddSingleton(new CosmosClient(
        options.Persistence.CosmosEndpoint,
        new DefaultAzureCredential(),
        new CosmosClientOptions { ApplicationName = "IC.BillerExperience.Api" }));
    builder.Services.AddSingleton<IBillerExperienceRepository>(services =>
        new CosmosBillerExperienceRepository(
            services.GetRequiredService<CosmosClient>(),
            options.Persistence.DatabaseName,
            services.GetRequiredService<ILogger<CosmosBillerExperienceRepository>>()));
}
else
{
    builder.Services.AddSingleton<IBillerExperienceRepository, InMemoryBillerExperienceRepository>();
}

if (string.Equals(options.Model.Provider, "AzureAI", StringComparison.OrdinalIgnoreCase))
{
    if (string.IsNullOrWhiteSpace(options.Model.Endpoint))
    {
        throw new InvalidOperationException("BillerExperience:Model:Endpoint is required for the AzureAI provider.");
    }
    builder.Services.AddSingleton(new AzureOpenAIClient(new Uri(options.Model.Endpoint), new DefaultAzureCredential()));
    builder.Services.AddSingleton(services => new AzureExperienceDraftGenerator(
        services.GetRequiredService<AzureOpenAIClient>(),
        options.Model.Deployment,
        services.GetRequiredService<ILogger<AzureExperienceDraftGenerator>>()));
    builder.Services.AddSingleton<IExperienceDraftGenerator, FallbackExperienceDraftGenerator>();
}
else
{
    builder.Services.AddSingleton<IExperienceDraftGenerator>(services =>
        services.GetRequiredService<DeterministicExperienceDraftGenerator>());
}

builder.Services.AddControllers().AddJsonOptions(json =>
{
    json.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    json.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
    json.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddHealthChecks();
builder.Services.AddCors(cors => cors.AddDefaultPolicy(policy =>
    policy.WithOrigins("http://localhost:5173", "http://localhost:5174")
        .AllowAnyHeader()
        .AllowAnyMethod()));

var openTelemetry = builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("IC.BillerExperience.Api"))
    .WithTracing(tracing => tracing
        .AddSource(BillerExperienceTelemetry.SourceName)
        .AddSource(OrchestrationTelemetry.ActivitySourceName))
    .WithMetrics(metrics => metrics
        .AddMeter(BillerExperienceTelemetry.MeterName)
        .AddMeter(OrchestrationTelemetry.MeterName));
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    openTelemetry.UseAzureMonitor();
}

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors();
app.MapControllers();
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

app.Run();
