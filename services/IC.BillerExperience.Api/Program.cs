using IC.Agentic.Orchestration.Abstractions;
using IC.Agentic.Orchestration.Execution;
using IC.BillerExperience.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IOrchestrationRunner, OrchestrationRunner>();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new ServiceInfo(
    "IC.BillerExperience.Api",
    "foundation",
    "Biller onboarding and experience publication")));
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

app.Run();
