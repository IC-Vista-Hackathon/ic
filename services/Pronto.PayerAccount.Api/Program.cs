using Pronto.PayerAccount.Api;
using Pronto.Persistence.Cosmos;
using Pronto.ServiceDefaults;
using Pronto.ServiceDefaults.Health;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("Pronto.PayerAccount.Api");
builder.Services.AddPayerAccountServices(builder.Configuration);

var persistence = builder.Configuration
    .GetSection(CosmosPersistenceOptions.SectionName)
    .Get<CosmosPersistenceOptions>() ?? new CosmosPersistenceOptions();

if (persistence.UseCosmos)
{
    builder.Services.AddHealthChecks().AddDependencyReadinessCheck(
        "cosmos", (services, _) => services.GetRequiredService<CosmosClient>().ReadAccountAsync());
}

var app = builder.Build();

app.UseServiceDefaults();

app.Run();

// Host bootstrap - configuration/DI wiring. Excluded from coverage metrics;
// behavior is exercised by integration and functional tests.
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public partial class Program
{
}
