using Microsoft.AspNetCore.Mvc;

namespace Pronto.BillerExperience.Api.Controllers;

/// <summary>
/// Runtime telemetry configuration for the browser frontends. The payer PWA fetches this at
/// startup so the Application Insights connection string never has to be baked into a frontend
/// build — the same pod env var that configures server-side Azure Monitor
/// (APPLICATIONINSIGHTS_CONNECTION_STRING) is handed to the browser SDK. A connection string is
/// an ingestion address, not a secret; browser SDKs ship it to every client by design.
/// </summary>
[ApiController]
[Route("public/telemetry")]
public sealed class TelemetryController(IConfiguration configuration) : ControllerBase
{
    public const string ConnectionStringKey = "APPLICATIONINSIGHTS_CONNECTION_STRING";
    public const string SamplingPercentageKey = "Telemetry:BrowserSamplingPercentage";

    [HttpGet]
    [ProducesResponseType<BrowserTelemetryConfigurationResponse>(StatusCodes.Status200OK)]
    public ActionResult<BrowserTelemetryConfigurationResponse> Get()
    {
        var connectionString = configuration[ConnectionStringKey];
        var samplingPercentage = configuration.GetValue(SamplingPercentageKey, 100d);

        // Cacheable: the value only changes on redeploy, and every payer session refetches it on load.
        Response.Headers.CacheControl = "public, max-age=300";
        return Ok(new BrowserTelemetryConfigurationResponse(
            string.IsNullOrWhiteSpace(connectionString) ? null : connectionString,
            Math.Clamp(samplingPercentage, 0d, 100d)));
    }
}

/// <summary>Wire shape is snake_case via the host's JSON options, matching every other endpoint.</summary>
public sealed record BrowserTelemetryConfigurationResponse(string? ConnectionString, double SamplingPercentage);
