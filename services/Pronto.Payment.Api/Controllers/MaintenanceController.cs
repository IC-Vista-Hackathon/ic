using Pronto.Payment.Api.Storage;
using Pronto.ServiceDefaults.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Pronto.Payment.Api.Controllers;

/// <summary>
/// Test-data maintenance. Lets functional tests running against a shared (Cosmos) environment
/// delete the payments and purchases they create. Disabled unless <c>Maintenance:PurgeEnabled</c>
/// is true (nonprod only) — returns 404 otherwise, so the route is invisible in prod.
/// </summary>
[ApiController]
[Route("internal/test-data")]
public sealed class MaintenanceController : ControllerBase
{
    private readonly IPaymentStore payments;
    private readonly IPurchaseStore purchases;
    private readonly MaintenanceOptions options;

    public MaintenanceController(
        IPaymentStore payments, IPurchaseStore purchases, IOptions<MaintenanceOptions> options)
    {
        this.payments = payments;
        this.purchases = purchases;
        this.options = options.Value;
    }

    /// <summary>
    /// Delete all payments and purchases for a biller. <c>DELETE /internal/test-data?biller_id=</c>.
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

        var trimmed = billerId.Trim();
        await payments.PurgeByBillerAsync(trimmed, cancellationToken);
        await purchases.PurgeByBillerAsync(trimmed, cancellationToken);
        return NoContent();
    }
}
