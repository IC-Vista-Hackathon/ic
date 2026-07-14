using IC.PayerAccount.Api.Storage;
using IC.Persistence.Cosmos;
using IC.ServiceDefaults;
using Microsoft.Azure.Cosmos;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddServiceDefaults();

var persistence = builder.Configuration
    .GetSection(CosmosPersistenceOptions.SectionName)
    .Get<CosmosPersistenceOptions>() ?? new CosmosPersistenceOptions();

if (persistence.UseCosmos)
{
    builder.Services.AddSingleton(CosmosClientFactory.Create(persistence, "IC.PayerAccount.Api"));
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
