using Pronto.Payment.Api.Clients;
using Pronto.Payment.Api.Storage;
using Pronto.Payment.Contracts.V1.Purchases;
using Pronto.ServiceDefaults.Errors;
using Microsoft.AspNetCore.Mvc;

namespace Pronto.Payment.Api.Controllers;

[ApiController]
[Route("purchases")]
public sealed class PurchasesController : ControllerBase
{
    private readonly IPurchaseStore store;
    private readonly IBillerAccountClient billerAccounts;

    public PurchasesController(IPurchaseStore store, IBillerAccountClient billerAccounts)
    {
        this.store = store;
        this.billerAccounts = billerAccounts;
    }

    [HttpPost]
    public async Task<ActionResult<PurchaseResponse>> Create(
        CreatePurchaseRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.BillerId))
        {
            throw ServiceException.BadRequest("biller_id_required", "biller_id is required");
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw ServiceException.BadRequest(
                "idempotency_key_required",
                "idempotency_key is required");
        }

        var pending = new PurchaseResponse(
            PurchaseId: Guid.NewGuid().ToString(),
            BillerId: request.BillerId.Trim(),
            Plan: request.Plan,
            AmountCents: request.Plan == PurchasePlan.Isolated ? 199900 : 49900,
            Status: PurchaseStatus.Pending);
        var reservation = await store.ReserveAsync(
            pending,
            request.IdempotencyKey.Trim(),
            cancellationToken).ConfigureAwait(false);
        if (reservation.Purchase.Status == PurchaseStatus.Paid)
        {
            return Ok(reservation.Purchase);
        }

        await billerAccounts.AdvanceToPurchasedAsync(
                reservation.Purchase.BillerId,
                reservation.Purchase.PurchaseId,
                reservation.Purchase.Plan,
                cancellationToken)
            .ConfigureAwait(false);
        var completed = await store.CompleteAsync(
            reservation.Purchase.BillerId,
            reservation.Purchase.PurchaseId,
            cancellationToken).ConfigureAwait(false);

        return reservation.Created
            ? Created($"/purchases/{completed.PurchaseId}?biller_id={completed.BillerId}", completed)
            : Ok(completed);
    }

    [HttpGet("{purchaseId}")]
    public async Task<ActionResult<PurchaseResponse>> Get(
        string purchaseId, [FromQuery(Name = "biller_id")] string billerId, CancellationToken cancellationToken)
        => await store.FindAsync(billerId, purchaseId, cancellationToken).ConfigureAwait(false)
            ?? throw ServiceException.NotFound("not_found", $"purchase {purchaseId} not found");
}
