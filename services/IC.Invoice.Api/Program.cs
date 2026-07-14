using IC.Invoice.Api;
using IC.Invoice.Api.Repositories;
using IC.ServiceDefaults;
using IC.Persistence.Cosmos;
using Microsoft.Azure.Cosmos;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("IC.Invoice.Api");
builder.Services.AddSingleton(TimeProvider.System);

var persistence = builder.Configuration
    .GetSection(CosmosPersistenceOptions.SectionName)
    .Get<CosmosPersistenceOptions>() ?? new CosmosPersistenceOptions();

if (persistence.UseCosmos)
{
    builder.Services.AddSingleton(CosmosClientFactory.Create(persistence, "IC.Invoice.Api"));
    builder.Services.AddSingleton<IInvoiceRepository>(services =>
        new CosmosInvoiceRepository(services.GetRequiredService<CosmosClient>(), persistence.DatabaseName));
}
else
{
    builder.Services.AddSingleton<IInvoiceRepository, InMemoryInvoiceRepository>();
}


var app = builder.Build();
app.MapGet("/", () => Results.Ok(new ServiceInfo("IC.Invoice.Api", "foundation", "Invoice seeding and lookup")));
app.UseServiceDefaults();
app.Run();

public partial class Program { }
