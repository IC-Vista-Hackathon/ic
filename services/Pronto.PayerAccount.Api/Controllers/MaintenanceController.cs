using Pronto.PayerAccount.Api.Storage;
using Pronto.ServiceDefaults.Errors;
using Pronto.ServiceDefaults.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Pronto.PayerAccount.Api.Controllers;

/// <summary>
/// Test-data maintenance. Lets functional tests running against a shared (Cosmos) environment
/// delete the payer accounts they create. Disabled unless <c>Maintenance:PurgeEnabled</c> is
/// true (nonprod only) — returns 404 otherwise, so the route is invisible in prod — and gated
/// by the maintenance role on top.
/// </summary>
[ApiController]
[Route("internal/test-data")]
[Authorize(Policy = ServiceAuthorization.Maintenance)]
public sealed class MaintenanceController : ControllerBase
{
    private readonly IPayerStore payers;
    private readonly MaintenanceOptions options;

    public MaintenanceController(IPayerStore payers, IOptions<MaintenanceOptions> options)
    {
        this.payers = payers;
        this.options = options.Value;
    }

    /// <summary>
    /// Delete all payer accounts for a biller. <c>DELETE /internal/test-data?biller_id=</c>.
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
            throw ServiceException.BadRequest("invalid_biller", "biller_id is required.");
        }

        await payers.PurgeByBillerAsync(billerId.Trim(), cancellationToken);
        return NoContent();
    }
}
