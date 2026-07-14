using Pronto.PayerAccount.Api;
using Pronto.PayerAccount.Api.Storage;
using Pronto.Persistence.Cosmos;
using Pronto.ServiceDefaults;
using Microsoft.Azure.Cosmos;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("Pronto.PayerAccount.Api");
builder.Services.Configure<MaintenanceOptions>(
    builder.Configuration.GetSection(MaintenanceOptions.SectionName));

var persistence = builder.Configuration
    .GetSection(CosmosPersistenceOptions.SectionName)
    .Get<CosmosPersistenceOptions>() ?? new CosmosPersistenceOptions();

if (persistence.UseCosmos)
{
    builder.Services.AddSingleton(CosmosClientFactory.Create(persistence, "Pronto.PayerAccount.Api"));
    builder.Services.AddSingleton<IPayerStore>(services =>
        new CosmosPayerStore(services.GetRequiredService<CosmosClient>(), persistence.DatabaseName));
}
else
{
    builder.Services.AddSingleton<IPayerStore, InMemoryPayerStore>();
}

var app = builder.Build();

app.UseServiceDefaults();

app.Run();

public partial class Program
{
}
