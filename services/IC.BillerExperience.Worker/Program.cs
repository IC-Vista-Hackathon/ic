using IC.BillerExperience.Worker;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHostedService<PublicationWorker>();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

await app.RunAsync();
