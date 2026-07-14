using Pronto.BillerExperience.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Pronto.BillerExperience.Api.Controllers;

/// <summary>
/// Test-data maintenance. Lets functional tests running against a shared (Cosmos) environment
/// delete the biller, experiences, runs, and deployments they create. Disabled unless
/// <c>Maintenance:PurgeEnabled</c> is true (nonprod only) — returns 404 otherwise, so the route
/// is invisible in prod.
/// </summary>
[ApiController]
[Route("internal/test-data")]
public sealed class MaintenanceController : ControllerBase
{
    private readonly IBillerExperienceRepository repository;
    private readonly MaintenanceOptions options;

    public MaintenanceController(IBillerExperienceRepository repository, IOptions<MaintenanceOptions> options)
    {
        this.repository = repository;
        this.options = options.Value;
    }

    /// <summary>
    /// Delete a biller and all its data. <c>DELETE /internal/test-data?biller_id=</c>.
    /// </summary>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Purge(
        [FromQuery(Name = "biller_id")] string? billerId,
        CancellationToken cancellationToken)
    {
        if (!options.PurgeEnabled)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(billerId))
        {
            return BadRequest(new { error = new { code = "invalid_biller", message = "biller_id is required." } });
        }

        await repository.PurgeByBillerAsync(billerId.Trim(), cancellationToken);
        return NoContent();
    }
}
