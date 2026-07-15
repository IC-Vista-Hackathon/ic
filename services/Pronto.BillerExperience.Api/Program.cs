using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.AI.OpenAI;
using Azure.AI.Projects;
using Azure.Core;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Azure.Storage.Blobs;
using Pronto.BillerExperience.Api;
using Pronto.BillerExperience.Api.Application;
using Pronto.BillerExperience.Api.Application.Agents;
using Pronto.BillerExperience.Api.Application.Compliance;
using Pronto.BillerExperience.Api.Configuration;
using Pronto.BillerExperience.Api.Infrastructure;
using Pronto.BillerExperience.Api.Infrastructure.AI;
using Pronto.BillerExperience.Api.Infrastructure.Compliance;
using Pronto.BillerExperience.Api.Infrastructure.Persistence;
using Pronto.BillerExperience.Api.Infrastructure.Mcp;
using Pronto.BillerExperience.Api.Infrastructure.Mcp.ServiceClients;
using Pronto.BillerExperience.Api.Infrastructure.Publication;
using Pronto.BillerExperience.Api.Infrastructure.Research;
using Pronto.BillerExperience.Api.Infrastructure.SupportingServices;
using Pronto.Agentic.Orchestration.Abstractions;
using Pronto.Agentic.Orchestration.Execution;
using Pronto.Agentic.Orchestration.Telemetry;
using Pronto.ServiceDefaults;
using Pronto.ServiceDefaults.Security;
using Microsoft.Azure.Cosmos;
using Microsoft.AspNetCore.Authorization;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceAuthentication();
builder.Services.PostConfigure<AuthorizationOptions>(authorization =>
    authorization.FallbackPolicy = null);
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
builder.Services.Configure<MaintenanceOptions>(builder.Configuration.GetSection(MaintenanceOptions.SectionName));
builder.Services.AddSingleton<IOrchestrationRunner, OrchestrationRunner>();
builder.Services.AddSingleton<BillerOnboardingService>();
builder.Services.AddSingleton<BillingDiscoveryEngine>();
builder.Services.AddSingleton<AgentContextService>();

// Payer pipeline (Bill Intelligence → Financial Planning → validate). Deterministic, Foundry-free
// stages behind their interfaces; the Azure planner slots in behind IFinancialPlanningAgent later.
builder.Services.AddSingleton<IBillIntelligenceAgent, DeterministicBillIntelligenceAgent>();
builder.Services.AddSingleton<IPaymentQuoteFetcher, PaymentQuoteFetcher>();
builder.Services.AddSingleton<IFinancialPlanningAgent, DeterministicFinancialPlanningAgent>();
builder.Services.AddSingleton<PaymentPlanValidator>();
builder.Services.AddSingleton<PayerChatService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AgentContextCapabilityService>();
builder.Services.AddSingleton<CompliancePolicyEngine>();
builder.Services.AddSingleton<IAgentContextCapabilityIssuer>(services =>
    services.GetRequiredService<AgentContextCapabilityService>());
builder.Services.AddSingleton<ServiceToolRegistry>();
builder.Services.AddMcpServer()
    .WithHttpTransport(transport => transport.Stateless = true)
    .WithTools<AgentContextMcpTools>()
    .WithTools<ServiceMcpTools>();
builder.Services.AddSingleton<DeterministicExperienceDraftGenerator>();
builder.Services.AddTransient<CorrelationPropagationHandler>();
builder.Services.AddSingleton<IDestinationAddressResolver, SystemDestinationAddressResolver>();
builder.Services.AddHttpClient<IBillerWebsiteResearcher, HttpBillerWebsiteResearcher>(client =>
    client.Timeout = Timeout.InfiniteTimeSpan)
    .ConfigurePrimaryHttpMessageHandler(ResearchHttpHandler.Create);
if (!string.IsNullOrWhiteSpace(options.Research.FoundryProjectEndpoint))
{
    builder.Services.AddSingleton<TokenCredential>(_ => new DefaultAzureCredential());
    builder.Services.AddSingleton(services => new AIProjectClient(
        new Uri(options.Research.FoundryProjectEndpoint),
        services.GetRequiredService<TokenCredential>()));
    builder.Services.AddSingleton<IFoundryAgentServiceGateway, FoundryAgentServiceGateway>();
    builder.Services.AddSingleton<FoundryResearchAgentAdapter>();
    builder.Services.AddSingleton<IResearchAgentCatalog>(services => services.GetRequiredService<FoundryResearchAgentAdapter>());
    builder.Services.AddSingleton<IResearchAgentDispatcher>(services => services.GetRequiredService<FoundryResearchAgentAdapter>());
    builder.Services.AddSingleton<IBillerResearchCoordinator>(services => new BillerResearchCoordinator(
        services.GetRequiredService<IResearchAgentCatalog>(),
        services.GetRequiredService<IResearchAgentDispatcher>(),
        services.GetRequiredService<Microsoft.Extensions.Options.IOptions<BillerExperienceOptions>>(),
        services.GetRequiredService<ILogger<BillerResearchCoordinator>>(),
        string.IsNullOrWhiteSpace(options.Research.CoordinatorAgentId)
            ? null
            : services.GetRequiredService<FoundryResearchAgentAdapter>(),
        services.GetRequiredService<IAgentContextCapabilityIssuer>()));
    if (options.AgentProvisioning.Enabled)
    {
        builder.Services.AddSingleton<IFoundryAgentAdministrationGateway, FoundryAgentAdministrationGateway>();
        builder.Services.AddHostedService<FoundryAgentReconciler>();
    }
}
else
{
    if (!string.IsNullOrWhiteSpace(options.Compliance.FoundryAgentId))
    {
        throw new InvalidOperationException(
            "BillerExperience:Research:FoundryProjectEndpoint is required when BillerExperience:Compliance:FoundryAgentId is configured.");
    }
    builder.Services.AddSingleton<IResearchAgentCatalog, LocalResearchAgentCatalog>();
    builder.Services.AddSingleton<IResearchAgentDispatcher, SameSiteResearchAgentDispatcher>();
    builder.Services.AddSingleton<IBillerResearchCoordinator>(services => new BillerResearchCoordinator(
        services.GetRequiredService<IResearchAgentCatalog>(),
        services.GetRequiredService<IResearchAgentDispatcher>(),
        services.GetRequiredService<Microsoft.Extensions.Options.IOptions<BillerExperienceOptions>>(),
        services.GetRequiredService<ILogger<BillerResearchCoordinator>>(),
        capabilityIssuer: services.GetRequiredService<IAgentContextCapabilityIssuer>()));
}
if (!string.IsNullOrWhiteSpace(options.Compliance.FoundryAgentId))
{
    builder.Services.AddSingleton<IComplianceKnowledgeReviewer, FoundryComplianceKnowledgeReviewer>();
}
builder.Services.AddSingleton<IComplianceReviewService>(services => new ComplianceReviewService(
    services.GetRequiredService<CompliancePolicyEngine>(),
    services.GetRequiredService<Microsoft.Extensions.Options.IOptions<BillerExperienceOptions>>(),
    services.GetRequiredService<ILogger<ComplianceReviewService>>(),
    services.GetService<IComplianceKnowledgeReviewer>()));
if (Uri.TryCreate(options.SupportingServices.InvoiceBaseUrl, UriKind.Absolute, out var invoiceBaseUri))
{
    builder.Services.AddHttpClient("invoice-seeder", client => client.BaseAddress = invoiceBaseUri)
        .AddHttpMessageHandler<CorrelationPropagationHandler>()
        .AddServiceBearerToken(builder.Configuration, builder.Environment);
    builder.Services.AddSingleton<IInvoiceSeeder>(services => new HttpInvoiceSeeder(
        services.GetRequiredService<IHttpClientFactory>().CreateClient("invoice-seeder"),
        services.GetRequiredService<ILogger<HttpInvoiceSeeder>>()));
}
else
{
    builder.Services.AddSingleton<IInvoiceSeeder, NullInvoiceSeeder>();
}

// Typed service clients behind the MCP service tools. Each is registered only when its
// downstream base URL is configured; otherwise a fail-fast UnavailableServiceClient stands in so
// the host still boots (and the corresponding tools return a clear "not configured" error).
if (Uri.TryCreate(options.SupportingServices.InvoiceBaseUrl, UriKind.Absolute, out var invoiceServiceUri))
{
    builder.Services.AddHttpClient("invoice-service", client => client.BaseAddress = invoiceServiceUri)
        .AddHttpMessageHandler<CorrelationPropagationHandler>();
    builder.Services.AddSingleton<IInvoiceServiceClient>(services => new HttpInvoiceServiceClient(
        services.GetRequiredService<IHttpClientFactory>().CreateClient("invoice-service")));
}
else
{
    builder.Services.AddSingleton<IInvoiceServiceClient>(new UnavailableServiceClient("Invoice service"));
}

if (Uri.TryCreate(options.SupportingServices.PaymentBaseUrl, UriKind.Absolute, out var paymentServiceUri))
{
    builder.Services.AddHttpClient("payment-service", client => client.BaseAddress = paymentServiceUri)
        .AddHttpMessageHandler<CorrelationPropagationHandler>();
    builder.Services.AddSingleton<IPaymentServiceClient>(services => new HttpPaymentServiceClient(
        services.GetRequiredService<IHttpClientFactory>().CreateClient("payment-service")));
}
else
{
    builder.Services.AddSingleton<IPaymentServiceClient>(new UnavailableServiceClient("Payment service"));
}

if (Uri.TryCreate(options.SupportingServices.PayerAccountBaseUrl, UriKind.Absolute, out var payerServiceUri))
{
    builder.Services.AddHttpClient("payer-account-service", client => client.BaseAddress = payerServiceUri)
        .AddHttpMessageHandler<CorrelationPropagationHandler>();
    builder.Services.AddSingleton<IPayerAccountServiceClient>(services => new HttpPayerAccountServiceClient(
        services.GetRequiredService<IHttpClientFactory>().CreateClient("payer-account-service")));
}
else
{
    builder.Services.AddSingleton<IPayerAccountServiceClient>(new UnavailableServiceClient("Payer Account service"));
}

if (!string.IsNullOrWhiteSpace(options.PublishedExperience.StorageEndpoint))
{
    builder.Services.AddSingleton(new BlobServiceClient(
        new Uri(options.PublishedExperience.StorageEndpoint),
        new DefaultAzureCredential()));
    builder.Services.AddSingleton(services => services.GetRequiredService<BlobServiceClient>()
        .GetBlobContainerClient(options.PublishedExperience.ContainerName));
    builder.Services.AddSingleton<IPublishedExperienceStore>(services => new BlobPublishedExperienceStore(
        services.GetRequiredService<Azure.Storage.Blobs.BlobContainerClient>(),
        services.GetRequiredService<ILogger<BlobPublishedExperienceStore>>()));
}
else
{
    builder.Services.AddSingleton<IPublishedExperienceStore, UnavailablePublishedExperienceStore>();
}

if (string.Equals(options.Persistence.Provider, "Cosmos", StringComparison.OrdinalIgnoreCase))
{
    if (string.IsNullOrWhiteSpace(options.Persistence.CosmosEndpoint))
    {
        throw new InvalidOperationException("BillerExperience:Persistence:CosmosEndpoint is required for the Cosmos provider.");
    }
    builder.Services.AddSingleton(new CosmosClient(
        options.Persistence.CosmosEndpoint,
        new DefaultAzureCredential(),
        new CosmosClientOptions { ApplicationName = "Pronto.BillerExperience.Api" }));
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
    json.JsonSerializerOptions.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
var healthChecks = builder.Services.AddHealthChecks();
if (!string.IsNullOrWhiteSpace(options.PublishedExperience.StorageEndpoint))
{
    healthChecks.AddCheck<PublishedExperienceHealthCheck>("published_experience_storage");
}
builder.Services.AddCors(cors => cors.AddDefaultPolicy(policy =>
    policy.WithOrigins("http://localhost:5173", "http://localhost:5174")
        .AllowAnyHeader()
        .AllowAnyMethod()));

builder.Services.FilterHealthProbeTraces();
var openTelemetry = builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("Pronto.BillerExperience.Api"))
    .WithTracing(tracing => tracing
        .AddSource(BillerExperienceTelemetry.SourceName)
        .AddSource(OrchestrationTelemetry.ActivitySourceName)
        .AddSource("Azure.AI.Projects.*"))
    .WithMetrics(metrics => metrics
        .AddMeter(BillerExperienceTelemetry.MeterName)
        .AddMeter(OrchestrationTelemetry.MeterName));
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    openTelemetry.UseAzureMonitor();
}

var app = builder.Build();

app.UseMiddleware<RequestObservabilityMiddleware>();
app.UseMiddleware<McpApiKeyMiddleware>();
app.UseExceptionHandler();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");
app.MapMcp("/mcp");

app.Run();

// Host bootstrap - configuration/DI wiring, only executable against live Azure.
// Excluded from coverage metrics; behavior is exercised by the functional tests.
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public partial class Program
{
}
