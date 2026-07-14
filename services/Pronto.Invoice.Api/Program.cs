using Pronto.Invoice.Api;
using Pronto.Invoice.Api.Repositories;
using Pronto.ServiceDefaults;
using Pronto.Persistence.Cosmos;
using Microsoft.Azure.Cosmos;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("Pronto.Invoice.Api");
builder.Services.AddSingleton(TimeProvider.System);

var persistence = builder.Configuration
    .GetSection(CosmosPersistenceOptions.SectionName)
    .Get<CosmosPersistenceOptions>() ?? new CosmosPersistenceOptions();

if (persistence.UseCosmos)
{
    builder.Services.AddSingleton(CosmosClientFactory.Create(persistence, "Pronto.Invoice.Api"));
    builder.Services.AddSingleton<IInvoiceRepository>(services =>
        new CosmosInvoiceRepository(services.GetRequiredService<CosmosClient>(), persistence.DatabaseName));
}
else
{
    builder.Services.AddSingleton<IInvoiceRepository, InMemoryInvoiceRepository>();
}


var app = builder.Build();
app.MapGet("/", () => Results.Ok(new ServiceInfo("Pronto.Invoice.Api", "foundation", "Invoice seeding and lookup")));
app.UseServiceDefaults();
app.Run();

public partial class Program { }
