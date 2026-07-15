using Pronto.Payment.Api.Purchases;
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
    private readonly PurchaseCompletionRunner completion;

    public PurchasesController(IPurchaseStore store, PurchaseCompletionRunner completion)
    {
        this.store = store;
        this.completion = completion;
    }

    [HttpPost]
    public async Task<ActionResult<PurchaseResponse>> Create(
        CreatePurchaseRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.BillerId))
        {
            throw ServiceException.BadRequest("validation_failed", "biller_id is required");
        }

        var created = await store.CreatePendingAsync(request, cancellationToken).ConfigureAwait(false);
        var purchase = created.Purchase;

        if (purchase.Status != PurchaseStatus.Paid)
        {
            var paid = await completion
                .TryCompleteAsync(
                    new PurchaseCompletion(purchase.BillerId, purchase.PurchaseId, purchase.Plan, Attempts: 0),
                    cancellationToken)
                .ConfigureAwait(false);
            purchase = paid ?? purchase;
        }

        var location = $"/purchases/{purchase.PurchaseId}?biller_id={purchase.BillerId}";
        return created.AlreadyExisted
            ? Ok(purchase)
            : Created(location, purchase);
    }

    [HttpGet("{purchaseId}")]
    public async Task<ActionResult<PurchaseResponse>> Get(
        string purchaseId, [FromQuery(Name = "biller_id")] string billerId, CancellationToken cancellationToken)
        => await store.FindAsync(billerId, purchaseId, cancellationToken).ConfigureAwait(false)
            ?? throw ServiceException.NotFound("not_found", $"purchase {purchaseId} not found");
}
