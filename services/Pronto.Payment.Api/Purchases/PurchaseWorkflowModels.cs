using Pronto.Payment.Contracts.V1.Purchases;

namespace Pronto.Payment.Api.Purchases;

/// <summary>
/// Result of <see cref="Storage.IPurchaseStore.CreatePendingAsync"/>. <see cref="AlreadyExisted"/>
/// is true when an idempotent retry matched an existing purchase rather than creating a new one.
/// </summary>
public sealed record PurchaseCreateResult(PurchaseResponse Purchase, bool AlreadyExisted);

/// <summary>
/// A durable completion (outbox) entry: the intent to advance BillerAccount.status to
/// <c>purchased</c> for a pending purchase. Persisted atomically with the purchase and drained
/// (with retry) until the cross-service write succeeds.
/// </summary>
public sealed record PurchaseCompletion(
    string BillerId,
    string PurchaseId,
    PurchasePlan Plan,
    int Attempts);
