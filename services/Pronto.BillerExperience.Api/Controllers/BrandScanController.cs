using Pronto.BillerExperience.Api.Infrastructure.Research;
using Pronto.BillerExperience.Contracts.V1.Branding;
using Microsoft.AspNetCore.Mvc;

namespace Pronto.BillerExperience.Api.Controllers;

/// <summary>
/// Reads a biller's public website during onboarding and returns the brand assets (colors, font,
/// logo) the Studio pre-fills on the Brand Details step. The scan runs server-side because a
/// browser cannot read another origin's CSS, and it reuses the SSRF-hardened same-site fetch path.
/// </summary>
[ApiController]
[Route("public/brand-scan")]
public sealed class BrandScanController(IBrandScanner scanner) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<BrandScanResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BrandScanResponse>> Scan(
        [FromBody] BrandScanRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Website is null || !request.Website.IsAbsoluteUri)
        {
            return BadRequest();
        }

        return Ok(await scanner.ScanAsync(request, cancellationToken));
    }
}
