using IC.Payment.Contracts.V1.Purchases;
using IC.ServiceDefaults.Errors;

namespace IC.Payment.Api.Storage;

public interface IPurchaseStore
{
    /// <summary>Adds the purchase; throws 409 already_purchased if the biller has one.</summary>
    void Add(PurchaseResponse purchase);

    PurchaseResponse? Find(string billerId, string purchaseId);
}

public sealed class InMemoryPurchaseStore : IPurchaseStore
{
    private readonly object gate = new();
    private readonly Dictionary<(string BillerId, string PurchaseId), PurchaseResponse> purchases = [];

    public void Add(PurchaseResponse purchase)
    {
        lock (gate)
        {
            if (purchases.Keys.Any(key => key.BillerId == purchase.BillerId))
            {
                throw ServiceException.Conflict(
                    "already_purchased", $"biller {purchase.BillerId} already purchased the platform");
            }

            purchases[(purchase.BillerId, purchase.PurchaseId)] = purchase;
        }
    }

    public PurchaseResponse? Find(string billerId, string purchaseId)
    {
        lock (gate)
        {
            return purchases.GetValueOrDefault((billerId, purchaseId));
        }
    }
}
