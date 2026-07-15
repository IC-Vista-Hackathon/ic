using Pronto.Payment.Api;
using Pronto.Payment.Api.Clients;
using Pronto.Payment.Api.Storage;
using Pronto.Persistence.Cosmos;
using Pronto.ServiceDefaults;
using Microsoft.Azure.Cosmos;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("Pronto.Payment.Api");
builder.Services.Configure<MaintenanceOptions>(
    builder.Configuration.GetSection(MaintenanceOptions.SectionName));

var persistence = builder.Configuration
    .GetSection(CosmosPersistenceOptions.SectionName)
    .Get<CosmosPersistenceOptions>() ?? new CosmosPersistenceOptions();

if (persistence.UseCosmos)
{
    builder.Services.AddSingleton(CosmosClientFactory.Create(persistence, "Pronto.Payment.Api"));
    builder.Services.AddSingleton<IPaymentStore>(services =>
        new CosmosPaymentStore(services.GetRequiredService<CosmosClient>(), persistence.DatabaseName));
    builder.Services.AddSingleton<IPurchaseStore>(services =>
        new CosmosPurchaseStore(services.GetRequiredService<CosmosClient>(), persistence.DatabaseName));
}
else
{
    builder.Services.AddSingleton<IPaymentStore, InMemoryPaymentStore>();
    builder.Services.AddSingleton<IPurchaseStore, InMemoryPurchaseStore>();
}

builder.Services.AddSingleton<IBillerConfigClient, DemoBillerConfigClient>();
builder.Services.AddSingleton<IBillerAccountClient, NoOpBillerAccountClient>();
builder.Services.AddHttpClient<IInvoiceClient, HttpInvoiceClient>(client =>
    client.BaseAddress = new Uri(
        builder.Configuration["Services:InvoiceApi"] ?? "http://localhost:5101"))
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
