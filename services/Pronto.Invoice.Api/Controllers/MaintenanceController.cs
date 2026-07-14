using Pronto.Invoice.Api.Common;
using Pronto.Invoice.Api.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Pronto.Invoice.Api.Controllers;

/// <summary>
/// Test-data maintenance. Lets functional tests running against a shared (Cosmos) environment
/// delete the data they create. Disabled unless <c>Maintenance:PurgeEnabled</c> is true (set
/// only in nonprod) — returns 404 otherwise, so the route is invisible in prod.
/// </summary>
[ApiController]
[Route("internal/test-data")]
public sealed class MaintenanceController : ControllerBase
{
    private readonly IInvoiceRepository _repository;
    private readonly MaintenanceOptions _options;

    public MaintenanceController(IInvoiceRepository repository, IOptions<MaintenanceOptions> options)
    {
        _repository = repository;
        _options = options.Value;
    }

    /// <summary>
    /// Delete all invoices for a biller. <c>DELETE /internal/test-data?biller_id=</c>.
    /// </summary>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Purge(
        [FromQuery(Name = "biller_id")] string? billerId,
        CancellationToken cancellationToken)
    {
        if (!_options.PurgeEnabled)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(billerId))
        {
            return BadRequest(ApiError.Of("invalid_biller", "biller_id is required."));
        }

        await _repository.PurgeByBillerAsync(billerId.Trim(), cancellationToken);
        return NoContent();
    }
}
