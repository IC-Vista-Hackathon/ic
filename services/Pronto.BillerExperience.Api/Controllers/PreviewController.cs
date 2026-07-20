using System.Diagnostics;
using Pronto.BillerExperience.Api.Application.Preview;
using Pronto.BillerExperience.Contracts.V1.Experiences;
using Pronto.BillerExperience.Contracts.V1.Preview;
using Microsoft.AspNetCore.Mvc;

namespace Pronto.BillerExperience.Api.Controllers;

/// <summary>
/// Studio preview tenant lifecycle (F2): provision/refresh and reset the isolated, seeded preview
/// tenant, and serve its current draft config to the built payer PWA. The preview thus runs the same
/// shipped bundle against the real services, backed by synthetic data in a preview-flagged partition.
/// </summary>
[ApiController]
public sealed partial class PreviewController(
    PreviewProvisioningService preview,
    ILogger<PreviewController> logger) : ControllerBase
{
    /// <summary>Provision (or refresh) the preview tenant for a biller and seed it with demo data.</summary>
    [HttpPost("billers/{billerId}/preview")]
    [ProducesResponseType<PreviewTenantResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PreviewTenantResponse>> Provision(
        string billerId, CancellationToken cancellationToken)
    {
        var descriptor = await preview.ProvisionAsync(billerId, cancellationToken);
        LogPreviewProvisioned(logger, billerId, descriptor.PreviewBillerId, Activity.Current?.TraceId.ToString());
        return Ok(descriptor);
    }

    /// <summary>Wipe + deterministically re-seed the preview tenant ("Restart preview").</summary>
    [HttpPost("billers/{billerId}/preview/reset")]
    [ProducesResponseType<PreviewTenantResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PreviewTenantResponse>> Reset(
        string billerId, CancellationToken cancellationToken)
    {
        var descriptor = await preview.ResetAsync(billerId, cancellationToken);
        LogPreviewReset(logger, billerId, descriptor.PreviewBillerId, Activity.Current?.TraceId.ToString());
        return Ok(descriptor);
    }

    /// <summary>
    /// Serve the preview tenant's current draft config to the built PWA (mirrors
    /// <c>/public/experiences/{slug}</c>, but reflects the in-progress draft rather than a published
    /// revision, with the <c>biller_id</c> pointed at the preview partition).
    /// </summary>
    [HttpGet("/public/experiences/preview/{previewBillerId}")]
    [ProducesResponseType<BillerExperienceDefinition>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BillerExperienceDefinition>> GetPreviewConfig(
        string previewBillerId, CancellationToken cancellationToken)
    {
        var definition = await preview.ResolvePreviewConfigAsync(previewBillerId, cancellationToken);
        Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        return Ok(definition);
    }

    [LoggerMessage(1600, LogLevel.Information, "Provisioned preview tenant {PreviewBillerId} for biller {BillerId}; trace {TraceId}")]
    private static partial void LogPreviewProvisioned(ILogger logger, string billerId, string previewBillerId, string? traceId);

    [LoggerMessage(1601, LogLevel.Information, "Reset preview tenant {PreviewBillerId} for biller {BillerId}; trace {TraceId}")]
    private static partial void LogPreviewReset(ILogger logger, string billerId, string previewBillerId, string? traceId);
}
