using System.Text.Json;
using Pronto.Invoice.Api;
using Pronto.Invoice.Api.Repositories;
using Pronto.Persistence.Cosmos;
using Microsoft.Azure.Cosmos;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        // Wire format is snake_case per design/contracts.md.
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
    });

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

builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new ServiceInfo(
    "Pronto.Invoice.Api",
    "foundation",
    "Invoice seeding and lookup")));
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");
app.MapControllers();

app.Run();
