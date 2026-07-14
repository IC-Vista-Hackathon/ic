using System.Text.Json;
using System.Text.Json.Serialization;
using Pronto.ServiceDefaults.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Pronto.ServiceDefaults;

public static class ServiceDefaultsExtensions
{
    /// <summary>
    /// Controllers + wire policy (snake_case, lowercase string enums — design/contracts.md,
    /// matching Pronto.Invoice.Api), error envelope for model-binding failures, and health checks.
    /// Every Pronto service host calls this.
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
        return services;
    }

    /// <summary>Error-envelope exception handling, controller routing, and health endpoints.</summary>
    public static WebApplication UseServiceDefaults(this WebApplication app)
    {
        app.UseMiddleware<ErrorEnvelopeMiddleware>();
        app.MapControllers();
        app.MapHealthChecks("/health/live");
        app.MapHealthChecks("/health/ready");
        return app;
    }
}
