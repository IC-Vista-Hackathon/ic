using Pronto.Payment.Api.Purchases;
using Pronto.Payment.Contracts.V1.Purchases;

namespace Pronto.Payment.Api.Storage;

public interface IPurchaseStore
{
    /// <summary>
    /// Atomically creates a pending purchase and its durable completion record. An explicit,
    /// matching idempotency key returns the existing purchase; all other repeats conflict.
    /// </summary>
    Task<PurchaseCreateResult> CreatePendingAsync(
        CreatePurchaseRequest request,
        CancellationToken cancellationToken = default);

    Task<PurchaseResponse?> FindAsync(
        string billerId,
        string purchaseId,
        CancellationToken cancellationToken = default);

    /// <summary>Delete all purchases in a biller's partition (nonprod test-cleanup only).</summary>
    Task PurgeByBillerAsync(string billerId, CancellationToken cancellationToken = default);
}
