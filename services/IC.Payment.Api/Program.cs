using IC.Payment.Api.Clients;
using IC.Payment.Api.Storage;
using IC.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddServiceDefaults();
builder.Services.AddSingleton<IPaymentStore, InMemoryPaymentStore>();
builder.Services.AddSingleton<IPurchaseStore, InMemoryPurchaseStore>();
builder.Services.AddSingleton<IBillerConfigClient, DemoBillerConfigClient>();
builder.Services.AddSingleton<IBillerAccountClient, NoOpBillerAccountClient>();
builder.Services.AddHttpClient<IInvoiceClient, HttpInvoiceClient>(client =>
    client.BaseAddress = new Uri(
        builder.Configuration["Services:InvoiceApi"] ?? "http://localhost:5101"));

var app = builder.Build();

app.UseServiceDefaults();

app.Run();

public partial class Program
{
}
