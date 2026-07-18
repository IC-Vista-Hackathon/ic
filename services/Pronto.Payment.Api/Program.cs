using Pronto.Payment.Api;
using Pronto.Payment.Api.Assurance;
using Pronto.Payment.Api.Clients;
using Pronto.Payment.Api.Workflow;
using Pronto.Persistence.Cosmos;
using Pronto.ServiceDefaults;
using Pronto.ServiceDefaults.Health;
using Pronto.ServiceDefaults.Security;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults(
    "Pronto.Payment.Api",
    meters: [PaymentTelemetry.MeterName, AssuranceTelemetry.MeterName]);
builder.Services.Configure<MaintenanceOptions>(
    builder.Configuration.GetSection(MaintenanceOptions.SectionName));

builder.Services.AddPaymentServices(builder.Configuration, builder.Environment);

var persistence = builder.Configuration
    .GetSection(CosmosPersistenceOptions.SectionName)
    .Get<CosmosPersistenceOptions>() ?? new CosmosPersistenceOptions();

if (persistence.UseCosmos)
{
    builder.Services.AddHealthChecks().AddDependencyReadinessCheck(
        "cosmos",
        async (services, cancellationToken) =>
        {
            var client = services.GetRequiredService<CosmosClient>();
            var database = client.GetDatabase(persistence.DatabaseName);
            await database.ReadAsync(cancellationToken: cancellationToken);
            await database.GetContainer("payments").ReadContainerAsync(cancellationToken: cancellationToken);
            await database.GetContainer("purchases").ReadContainerAsync(cancellationToken: cancellationToken);
        });
}

if (builder.Environment.IsProduction())
{
    AddHttpReadinessCheck(builder, "invoice-api", builder.Configuration["Services:InvoiceApi"]!);
    AddHttpReadinessCheck(
        builder,
        "payer-account-api",
        builder.Configuration["Services:PayerAccountApi"]!);
}

builder.Services.AddPurchaseWorkflow(builder.Configuration);
builder.Services.RemoveAll<IBillerAccountClient>();
builder.Services.AddHttpClient<IBillerAccountClient, HttpBillerAccountClient>(client =>
    client.BaseAddress = new Uri(
        builder.Configuration["Services:BillerExperienceApi"] ?? "http://localhost:5000"))
    .AddHttpMessageHandler<CorrelationPropagationHandler>()
    .AddServiceBearerToken(builder.Configuration, builder.Environment);

var app = builder.Build();

app.UseServiceDefaults();

app.Run();

static void AddHttpReadinessCheck(
    WebApplicationBuilder builder,
    string name,
    string baseAddress)
{
    var clientName = $"readiness-{name}";
    builder.Services.AddHttpClient(clientName, client =>
        client.BaseAddress = new Uri(baseAddress));
    builder.Services.AddHealthChecks().AddDependencyReadinessCheck(
        name,
        async (services, cancellationToken) =>
        {
            var client = services.GetRequiredService<IHttpClientFactory>()
                .CreateClient(clientName);
            using var response = await client.GetAsync("health/ready", cancellationToken);
            response.EnsureSuccessStatusCode();
        });
}

// Host bootstrap - configuration/DI wiring. Excluded from coverage metrics;
// behavior is exercised by integration and functional tests.
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public partial class Program
{
}
