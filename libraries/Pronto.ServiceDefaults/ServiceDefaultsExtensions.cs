using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Pronto.ServiceDefaults.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Resources;

namespace Pronto.ServiceDefaults;

public static class ServiceDefaultsExtensions
{
    public static WebApplicationBuilder AddServiceDefaults(this WebApplicationBuilder builder, string serviceName)
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
            options.UseUtcTimestamp = true;
        });
        builder.Services.AddServiceDefaults();
        builder.Services.FilterHealthProbeTraces();
        var telemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName));
        if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        {
            telemetry.UseAzureMonitor();
        }
        return builder;
    }

    /// <summary>
    /// Health probes (<c>/health/live</c>, <c>/health/ready</c>) are polled continuously by
    /// Kubernetes; excluding them from ASP.NET Core trace collection keeps that noise out of
    /// Application Insights while normal requests are still exported. Idempotent, and a no-op
    /// unless the AspNetCore instrumentation is active (e.g. via Azure Monitor).
    /// </summary>
    public static IServiceCollection FilterHealthProbeTraces(this IServiceCollection services)
    {
        services.Configure<AspNetCoreTraceInstrumentationOptions>(options =>
            options.Filter = context => !IsHealthProbeRequest(context));
        return services;
    }

    /// <summary>True for the liveness/readiness probe endpoints that should not be traced.</summary>
    public static bool IsHealthProbeRequest(HttpContext context)
    {
        var path = context.Request.Path;
        return path.Equals("/health/live", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/health/ready", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Controllers + wire policy (snake_case, lowercase string enums — design/contracts.md,
    /// matching Pronto.Invoice.Api), error envelope for model-binding failures, and health checks.
    /// Every IC service host calls this.
    /// </summary>
    public static IServiceCollection AddServiceDefaults(this IServiceCollection services)
    {
        services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
                options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
                options.JsonSerializerOptions.Converters.Add(
                    new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
            })
            .ConfigureApiBehaviorOptions(options =>
                options.InvalidModelStateResponseFactory = context =>
                {
                    var message = context.ModelState
                        .Where(entry => entry.Value?.Errors.Count > 0)
                        .Select(entry => $"{entry.Key}: {entry.Value!.Errors[0].ErrorMessage}")
                        .FirstOrDefault() ?? "Request validation failed.";

                    return new BadRequestObjectResult(
                        new ErrorEnvelope(new ErrorDetail("validation_failed", message)));
                });

        services.AddHealthChecks();
        services.AddTransient<CorrelationPropagationHandler>();
        return services;
    }

    /// <summary>Error-envelope exception handling, controller routing, and health endpoints.</summary>
    public static WebApplication UseServiceDefaults(this WebApplication app)
    {
        app.UseMiddleware<RequestObservabilityMiddleware>();
        app.UseMiddleware<ErrorEnvelopeMiddleware>();
        app.MapControllers();
        app.MapHealthChecks("/health/live");
        app.MapHealthChecks("/health/ready");
        return app;
    }
}
