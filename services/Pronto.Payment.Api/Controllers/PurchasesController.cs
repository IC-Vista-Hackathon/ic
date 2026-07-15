using Pronto.Payment.Api.Clients;
using Pronto.Payment.Api.Storage;
using Pronto.Payment.Contracts.V1.Purchases;
using Pronto.ServiceDefaults.Errors;
using Pronto.ServiceDefaults.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Pronto.Payment.Api.Controllers;

[ApiController]
[Route("purchases")]
[Authorize]
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
        BillerClaims.RequireBillerAccess(User, request.BillerId);

        var purchase = new PurchaseResponse(
            PurchaseId: Guid.NewGuid().ToString(),
            BillerId: request.BillerId,
            Plan: request.Plan,
            AmountCents: request.Plan == PurchasePlan.Isolated ? 199900 : 49900,
            Status: PurchaseStatus.Paid);
        await store.AddAsync(purchase, cancellationToken).ConfigureAwait(false);

        await billerAccounts.AdvanceToPurchasedAsync(request.BillerId, request.Plan, cancellationToken)
            .ConfigureAwait(false);

        return Created($"/purchases/{purchase.PurchaseId}?biller_id={purchase.BillerId}", purchase);
    }

    [HttpGet("{purchaseId}")]
    public async Task<ActionResult<PurchaseResponse>> Get(
        string purchaseId, [FromQuery(Name = "biller_id")] string billerId, CancellationToken cancellationToken)
    {
        BillerClaims.RequireBillerAccess(User, billerId);
        return await store.FindAsync(billerId, purchaseId, cancellationToken).ConfigureAwait(false)
            ?? throw ServiceException.NotFound("not_found", $"purchase {purchaseId} not found");
    }
}
