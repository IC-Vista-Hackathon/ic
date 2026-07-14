using IC.Payment.Api.Clients;
using IC.Payment.Api.Storage;
using IC.Persistence.Cosmos;
using IC.ServiceDefaults;
using Microsoft.Azure.Cosmos;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("IC.Payment.Api");

var persistence = builder.Configuration
    .GetSection(CosmosPersistenceOptions.SectionName)
    .Get<CosmosPersistenceOptions>() ?? new CosmosPersistenceOptions();

if (persistence.UseCosmos)
{
    builder.Services.AddSingleton(CosmosClientFactory.Create(persistence, "IC.Payment.Api"));
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

public partial class Program
{
}
