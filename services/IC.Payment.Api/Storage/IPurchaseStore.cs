using IC.Payment.Contracts.V1.Purchases;
using IC.ServiceDefaults.Errors;

namespace IC.Payment.Api.Storage;

public interface IPurchaseStore
{
    /// <summary>Adds the purchase; throws 409 already_purchased if the biller has one.</summary>
    Task AddAsync(PurchaseResponse purchase, CancellationToken cancellationToken = default);

    Task<PurchaseResponse?> FindAsync(string billerId, string purchaseId, CancellationToken cancellationToken = default);

    /// <summary>Delete all purchases in a biller's partition (nonprod test-cleanup only).</summary>
    Task PurgeByBillerAsync(string billerId, CancellationToken cancellationToken = default);
}

public sealed class InMemoryPurchaseStore : IPurchaseStore
{
    private readonly object gate = new();
    private readonly Dictionary<(string BillerId, string PurchaseId), PurchaseResponse> purchases = [];

    public Task AddAsync(PurchaseResponse purchase, CancellationToken cancellationToken = default)
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

        return Task.CompletedTask;
    }

    public Task<PurchaseResponse?> FindAsync(
        string billerId, string purchaseId, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            return Task.FromResult(purchases.GetValueOrDefault((billerId, purchaseId)));
        }
    }

    public Task PurgeByBillerAsync(string billerId, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            foreach (var key in purchases.Keys.Where(k => k.BillerId == billerId).ToList())
            {
                purchases.Remove(key);
            }
        }

        return Task.CompletedTask;
    }
}
