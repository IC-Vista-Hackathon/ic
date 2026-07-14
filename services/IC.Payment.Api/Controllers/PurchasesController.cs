using IC.Payment.Api.Clients;
using IC.Payment.Api.Storage;
using IC.Payment.Contracts.V1.Purchases;
using IC.ServiceDefaults.Errors;
using Microsoft.AspNetCore.Mvc;

namespace IC.Payment.Api.Controllers;

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
        var purchase = new PurchaseResponse(
            PurchaseId: Guid.NewGuid().ToString(),
            BillerId: request.BillerId,
            Plan: request.Plan,
            AmountCents: request.Plan == PurchasePlan.Isolated ? 199900 : 49900,
            Status: PurchaseStatus.Paid);
        store.Add(purchase);

        await billerAccounts.AdvanceToPurchasedAsync(request.BillerId, request.Plan, cancellationToken)
            .ConfigureAwait(false);

        return Created($"/purchases/{purchase.PurchaseId}?billerId={purchase.BillerId}", purchase);
    }

    [HttpGet("{purchaseId}")]
    public ActionResult<PurchaseResponse> Get(string purchaseId, [FromQuery] string billerId)
        => store.Find(billerId, purchaseId)
            ?? throw ServiceException.NotFound("not_found", $"purchase {purchaseId} not found");
}
