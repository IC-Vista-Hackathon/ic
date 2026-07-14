using IC.Invoice.Api;
using IC.Invoice.Api.Repositories;
using IC.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("IC.Invoice.Api");
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IInvoiceRepository, InMemoryInvoiceRepository>();

var app = builder.Build();
app.MapGet("/", () => Results.Ok(new ServiceInfo("IC.Invoice.Api", "foundation", "Invoice seeding and lookup")));
app.UseServiceDefaults();
app.Run();

public partial class Program { }
