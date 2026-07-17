using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Pronto.ServiceDefaults.Errors;
using Pronto.ServiceDefaults.Health;
using Pronto.ServiceDefaults.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Pronto.ServiceDefaults;

public static class ServiceDefaultsExtensions
{
    public static WebApplicationBuilder AddServiceDefaults(
        this WebApplicationBuilder builder,
        string serviceName,
        IReadOnlyList<string>? activitySources = null,
        IReadOnlyList<string>? meters = null)
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
            options.UseUtcTimestamp = true;
        });
        builder.Services.AddServiceDefaults();
        builder.AddServiceAuthentication();
        builder.Services.FilterHealthProbeTraces();
        var telemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName));
        if (activitySources is { Count: > 0 })
        {
            telemetry.WithTracing(tracing =>
            {
                foreach (var source in activitySources)
                {
                    tracing.AddSource(source);
                }
            });
        }
        if (meters is { Count: > 0 })
        {
            telemetry.WithMetrics(metrics =>
            {
                foreach (var meter in meters)
                {
                    metrics.AddMeter(meter);
                }
            });
        }
        telemetry.AddAzureMonitorExporter(builder.Configuration);
        return builder;
    }

    /// <summary>
    /// Adds the Azure Monitor exporter with the fixed-rate trace sampling ratio applied, but only
    /// when <c>APPLICATIONINSIGHTS_CONNECTION_STRING</c> is set. This is the single place the whole
    /// solution wires Azure Monitor + sampling, so hosts that register OpenTelemetry directly
    /// (e.g. the orchestration API and worker, which add their own ActivitySources/Meters) honour
    /// <c>APPLICATIONINSIGHTS_SAMPLING_RATIO</c> identically to hosts that use AddServiceDefaults.
    /// </summary>
    public static OpenTelemetryBuilder AddAzureMonitorExporter(
        this OpenTelemetryBuilder telemetry,
        IConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        {
            var samplingRatio = ResolveSamplingRatio(configuration["APPLICATIONINSIGHTS_SAMPLING_RATIO"]);
            telemetry.UseAzureMonitor(options => options.SamplingRatio = samplingRatio);
        }
        return telemetry;
    }

    /// <summary>
    /// Fixed-rate trace sampling ratio for Azure Monitor, as headroom against the Application
    /// Insights daily ingestion cap. Defaults to 1.0 (keep everything) so behaviour is unchanged
    /// unless <c>APPLICATIONINSIGHTS_SAMPLING_RATIO</c> is set for an environment (e.g. <c>0.25</c>
    /// keeps ~25% of traces). Azure Monitor's sampler is parent-consistent — a sampled-out request
    /// drops its whole trace, so ratios stay meaningful across services. Values outside (0, 1] or
    /// unparseable input fall back to 1.0 rather than silently disabling telemetry.
    /// </summary>
    internal static float ResolveSamplingRatio(string? configuredValue)
    {
        const float keepEverything = 1.0f;
        if (string.IsNullOrWhiteSpace(configuredValue)
            || !float.TryParse(configuredValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var ratio)
            || !(ratio > 0f && ratio <= 1f))
        {
            return keepEverything;
        }
        return ratio;
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

        // Liveness is a trivial "process is up" check (tagged live); readiness is composed from
        // the dependency probes hosts register with AddDependencyReadinessCheck (tagged ready).
        services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy("process alive"), tags: [HealthTags.Live]);
        services.AddTransient<CorrelationPropagationHandler>();
        return services;
    }

    /// <summary>
    /// Error-envelope exception handling, authentication/authorization, controller routing, and
    /// the liveness/readiness health endpoints. Controllers are covered by the fail-closed
    /// fallback policy; health probes are explicitly anonymous.
    /// </summary>
    public static WebApplication UseServiceDefaults(this WebApplication app)
    {
        app.UseMiddleware<RequestObservabilityMiddleware>();
        app.UseMiddleware<ErrorEnvelopeMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();

        // Liveness: process is up, independent of dependencies (a failing dependency must not
        // trigger pod restarts). Readiness: dependency probes must pass before taking traffic.
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(HealthTags.Live),
        }).AllowAnonymous();
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(HealthTags.Ready),
        }).AllowAnonymous();
        return app;
    }
}
