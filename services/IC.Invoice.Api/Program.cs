using IC.Invoice.Api.Storage;
using IC.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddServiceDefaults();
builder.Services.AddSingleton<IInvoiceStore, InMemoryInvoiceStore>();

var app = builder.Build();

app.UseServiceDefaults();

app.Run();

public partial class Program
{
}
