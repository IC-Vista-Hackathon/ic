using Pronto.Payment.Api;
using Pronto.Payment.Api.Clients;
using Pronto.Persistence.Cosmos;
using Pronto.ServiceDefaults;
using Pronto.ServiceDefaults.Health;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("Pronto.Payment.Api");
builder.Services.Configure<MaintenanceOptions>(
    builder.Configuration.GetSection(MaintenanceOptions.SectionName));

builder.Services.AddPaymentServices(builder.Configuration);

var persistence = builder.Configuration
    .GetSection(CosmosPersistenceOptions.SectionName)
    .Get<CosmosPersistenceOptions>() ?? new CosmosPersistenceOptions();

if (persistence.UseCosmos)
{
    builder.Services.AddHealthChecks().AddDependencyReadinessCheck(
        "cosmos", (services, _) => services.GetRequiredService<CosmosClient>().ReadAccountAsync());
}

builder.Services.RemoveAll<IBillerAccountClient>();
builder.Services.AddHttpClient<IBillerAccountClient, HttpBillerAccountClient>(client =>
    client.BaseAddress = new Uri(
        builder.Configuration["Services:BillerExperienceApi"] ?? "http://localhost:5000"))
    .AddHttpMessageHandler<CorrelationPropagationHandler>();

var app = builder.Build();

app.UseServiceDefaults();

app.Run();

// Host bootstrap - configuration/DI wiring. Excluded from coverage metrics;
// behavior is exercised by integration and functional tests.
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public partial class Program
{
}
