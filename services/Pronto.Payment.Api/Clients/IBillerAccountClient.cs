using Pronto.Payment.Contracts.V1.Purchases;

namespace Pronto.Payment.Api.Clients;

/// <summary>
/// Advances the BillerAccount owned by Biller Configuration Service after purchase. Implementors
/// must treat <paramref name="idempotencyKey"/> idempotently because a successful downstream
/// transition can be retried before the local purchase is marked paid.
/// </summary>
public interface IBillerAccountClient
{
    Task AdvanceToPurchasedAsync(
        string billerId,
        PurchasePlan plan,
        string idempotencyKey,
        CancellationToken cancellationToken);
}

/// <summary>
/// Fail-closed default used until a parent host provides the Biller Configuration Service client.
/// Throwing keeps the purchase pending and durably queued rather than falsely reporting paid.
/// </summary>
public sealed class UnavailableBillerAccountClient : IBillerAccountClient
{
    public Task AdvanceToPurchasedAsync(
        string billerId,
        PurchasePlan plan,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        Task.FromException(new InvalidOperationException(
            "BillerAccount completion client is not configured; purchase remains queued for retry."));
}
