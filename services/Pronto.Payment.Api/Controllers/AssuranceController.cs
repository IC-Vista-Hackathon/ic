using Pronto.Payment.Api.Assurance;
using Pronto.ServiceDefaults.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Pronto.Payment.Api.Controllers;

/// <summary>
/// Triggerable post-publish assurance. Lets an operator (or a schedule) run the synthetic canary
/// and ledger reconciliation on demand and read their structured results. Gated by the maintenance
/// role and disabled (404) unless <c>Assurance:EndpointsEnabled</c> is true, so the routes are
/// invisible where they aren't wanted. The continuous variant is <see cref="AssuranceWorker"/>.
/// </summary>
[ApiController]
[Route("internal/assurance")]
[Authorize(Policy = ServiceAuthorization.Maintenance)]
public sealed class AssuranceController : ControllerBase
{
    private readonly PaymentReconciliationService reconciler;
    private readonly CanaryPaymentRunner canary;
    private readonly ICanaryTargetSource canaryTargets;
    private readonly AssuranceOptions options;

    public AssuranceController(
        PaymentReconciliationService reconciler,
        CanaryPaymentRunner canary,
        ICanaryTargetSource canaryTargets,
        IOptions<AssuranceOptions> options)
    {
        this.reconciler = reconciler;
        this.canary = canary;
        this.canaryTargets = canaryTargets;
        this.options = options.Value;
    }

    /// <summary>
    /// Run a reconciliation pass. <c>POST /internal/assurance/reconcile?biller_id=</c> with an
    /// optional body of UI-claimed confirmations. Returns the structured result; the response
    /// status is 200 on a clean ledger and 409 when a divergence is found, so a caller (or the
    /// functional suite) can treat divergence as a failure signal.
    /// </summary>
    [HttpPost("reconcile")]
    [ProducesResponseType(typeof(ReconciliationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ReconciliationResult), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReconciliationResult>> Reconcile(
        [FromQuery(Name = "biller_id")] string? billerId,
        [FromBody] ReconciliationRequest? request,
        CancellationToken cancellationToken)
    {
        if (!options.EndpointsEnabled)
        {
            return NotFound();
        }

        var trimmed = string.IsNullOrWhiteSpace(billerId) ? null : billerId.Trim();
        var result = await reconciler.ReconcileAsync(trimmed, request, cancellationToken);
        return result.Ok ? Ok(result) : Conflict(result);
    }

    /// <summary>
    /// Run the synthetic canary against every configured target. <c>POST /internal/assurance/canary</c>.
    /// Returns per-target outcomes; the response status is 200 when all settle and 409 otherwise.
    /// </summary>
    [HttpPost("canary")]
    [ProducesResponseType(typeof(CanaryRunResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CanaryRunResult), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CanaryRunResult>> RunCanary(CancellationToken cancellationToken)
    {
        if (!options.EndpointsEnabled)
        {
            return NotFound();
        }

        var targets = await canaryTargets.GetTargetsAsync(cancellationToken);
        var result = await canary.RunAllAsync(targets, cancellationToken);
        return result.Ok ? Ok(result) : Conflict(result);
    }
}
