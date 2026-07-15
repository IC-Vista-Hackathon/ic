using Pronto.Payment.Api;
using Pronto.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("Pronto.Payment.Api");
builder.Services.Configure<MaintenanceOptions>(
    builder.Configuration.GetSection(MaintenanceOptions.SectionName));

// Whole Payment Service capability (stores, clients, recoverable workflow, payer-account
// validation, scheduled-payment processor) — see PaymentServiceCollectionExtensions.
builder.Services.AddPaymentServices(builder.Configuration);

var app = builder.Build();

app.UseServiceDefaults();

app.Run();

// Host bootstrap - configuration/DI wiring. Excluded from coverage metrics;
// behavior is exercised by integration and functional tests.
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public partial class Program
{
}
