using Pronto.Invoice.Api;
using Pronto.Invoice.Api.Repositories;
using Pronto.ServiceDefaults;
using Pronto.ServiceDefaults.Health;
using Pronto.Persistence.Cosmos;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("Pronto.Invoice.Api");
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<MaintenanceOptions>(
    builder.Configuration.GetSection(MaintenanceOptions.SectionName));

var persistence = builder.Configuration
    .GetSection(CosmosPersistenceOptions.SectionName)
    .Get<CosmosPersistenceOptions>() ?? new CosmosPersistenceOptions();

if (persistence.UseCosmos)
{
    builder.Services.AddSingleton(CosmosClientFactory.Create(persistence, "Pronto.Invoice.Api"));
    builder.Services.AddSingleton<IInvoiceRepository>(services =>
        new CosmosInvoiceRepository(services.GetRequiredService<CosmosClient>(), persistence.DatabaseName));
    builder.Services.AddHealthChecks().AddDependencyReadinessCheck(
        "cosmos", (services, _) => services.GetRequiredService<CosmosClient>().ReadAccountAsync());
}
else
{
    builder.Services.AddSingleton<IInvoiceRepository, InMemoryInvoiceRepository>();
}


var app = builder.Build();
app.MapGet("/", () => Results.Ok(new ServiceInfo("Pronto.Invoice.Api", "foundation", "Invoice seeding and lookup")))
    .AllowAnonymous();
app.UseServiceDefaults();
app.Run();

// Host bootstrap - configuration/DI wiring. Excluded from coverage metrics;
// behavior is exercised by integration and functional tests.
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public partial class Program
{
}
