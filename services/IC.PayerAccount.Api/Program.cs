using IC.PayerAccount.Api.Storage;
using IC.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("IC.PayerAccount.Api");
builder.Services.AddSingleton<IPayerStore, InMemoryPayerStore>();

var app = builder.Build();

app.UseServiceDefaults();

app.Run();

public partial class Program
{
}
