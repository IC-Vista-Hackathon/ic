using System.Text.Json;
using IC.Invoice.Api;
using IC.Invoice.Api.Repositories;

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
builder.Services.AddSingleton<IInvoiceRepository, InMemoryInvoiceRepository>();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new ServiceInfo(
    "IC.Invoice.Api",
    "foundation",
    "Invoice seeding and lookup")));
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");
app.MapControllers();

app.Run();
