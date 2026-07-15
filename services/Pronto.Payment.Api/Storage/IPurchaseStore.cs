using Pronto.Payment.Api.Purchases;
using Pronto.Payment.Contracts.V1.Purchases;

namespace Pronto.Payment.Api.Storage;

public interface IPurchaseStore
{
    Task<PurchaseCreateResult> CreatePendingAsync(
        CreatePurchaseRequest request,
        CancellationToken cancellationToken = default);

    Task<PurchaseResponse?> FindAsync(
        string billerId,
        string purchaseId,
        CancellationToken cancellationToken = default);

    Task PurgeByBillerAsync(string billerId, CancellationToken cancellationToken = default);
}
