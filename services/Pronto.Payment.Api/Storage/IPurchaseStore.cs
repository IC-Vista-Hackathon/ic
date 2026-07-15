using Pronto.Payment.Contracts.V1.Purchases;
using Pronto.ServiceDefaults.Errors;

namespace Pronto.Payment.Api.Storage;

public interface IPurchaseStore
{
    Task<PurchaseReservation> ReserveAsync(
        PurchaseResponse purchase,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<PurchaseResponse> CompleteAsync(
        string billerId,
        string purchaseId,
        CancellationToken cancellationToken = default);

    Task<PurchaseResponse?> FindAsync(string billerId, string purchaseId, CancellationToken cancellationToken = default);

    /// <summary>Delete all purchases in a biller's partition (nonprod test-cleanup only).</summary>
    Task PurgeByBillerAsync(string billerId, CancellationToken cancellationToken = default);
}

public sealed record PurchaseReservation(PurchaseResponse Purchase, bool Created);

public sealed class InMemoryPurchaseStore : IPurchaseStore
{
    private readonly object gate = new();
    private readonly Dictionary<string, StoredPurchase> purchases = [];

    public Task<PurchaseReservation> ReserveAsync(
        PurchaseResponse purchase,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (purchases.TryGetValue(purchase.BillerId, out var existing))
            {
                if (existing.IdempotencyKey == idempotencyKey
                    && existing.Purchase.Plan == purchase.Plan)
                {
                    return Task.FromResult(new PurchaseReservation(existing.Purchase, Created: false));
                }

                throw AlreadyPurchased(purchase.BillerId);
            }

            purchases[purchase.BillerId] = new StoredPurchase(purchase, idempotencyKey);
            return Task.FromResult(new PurchaseReservation(purchase, Created: true));
        }
    }

    public Task<PurchaseResponse> CompleteAsync(
        string billerId,
        string purchaseId,
        CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (!purchases.TryGetValue(billerId, out var existing)
                || existing.Purchase.PurchaseId != purchaseId)
            {
                throw ServiceException.NotFound("not_found", $"purchase {purchaseId} not found");
            }

            var completed = existing.Purchase with { Status = PurchaseStatus.Paid };
            purchases[billerId] = existing with { Purchase = completed };
            return Task.FromResult(completed);
        }
    }

    public Task<PurchaseResponse?> FindAsync(
        string billerId, string purchaseId, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var purchase = purchases.GetValueOrDefault(billerId)?.Purchase;
            return Task.FromResult(purchase?.PurchaseId == purchaseId ? purchase : null);
        }
    }

    public Task PurgeByBillerAsync(string billerId, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            purchases.Remove(billerId);
        }

        return Task.CompletedTask;
    }

    private static ServiceException AlreadyPurchased(string billerId) =>
        ServiceException.Conflict(
            "already_purchased",
            $"biller {billerId} already purchased or started purchasing the platform");

    private sealed record StoredPurchase(PurchaseResponse Purchase, string IdempotencyKey);
}
