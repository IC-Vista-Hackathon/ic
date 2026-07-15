using Pronto.PayerAccount.Api;
using Pronto.Persistence.Cosmos;
using Pronto.ServiceDefaults;
using Pronto.ServiceDefaults.Health;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("Pronto.PayerAccount.Api");
builder.Services.AddPayerAccountServices(builder.Configuration, builder.Environment);

var persistence = builder.Configuration
    .GetSection(CosmosPersistenceOptions.SectionName)
    .Get<CosmosPersistenceOptions>() ?? new CosmosPersistenceOptions();

if (persistence.UseCosmos)
{
    builder.Services.AddHealthChecks().AddDependencyReadinessCheck(
        "cosmos",
        async (services, cancellationToken) =>
        {
            var database = services.GetRequiredService<CosmosClient>()
                .GetDatabase(persistence.DatabaseName);
            await database.ReadAsync(cancellationToken: cancellationToken);
            await database.GetContainer("payer_accounts")
                .ReadContainerAsync(cancellationToken: cancellationToken);
        });
}

if (builder.Environment.IsProduction())
{
    const string readinessClient = "readiness-invoice-api";
    builder.Services.AddHttpClient(readinessClient, client =>
        client.BaseAddress = new Uri(builder.Configuration["Services:InvoiceApi"]!));
    builder.Services.AddHealthChecks().AddDependencyReadinessCheck(
        "invoice-api",
        async (services, cancellationToken) =>
        {
            var client = services.GetRequiredService<IHttpClientFactory>()
                .CreateClient(readinessClient);
            using var response = await client.GetAsync("health/ready", cancellationToken);
            response.EnsureSuccessStatusCode();
        });
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
