using Pronto.Payment.Contracts.V1.Purchases;
using Pronto.ServiceDefaults.Errors;

namespace Pronto.Payment.Api.Storage;

/// <summary>
/// Decides whether a create for a biller that already has a purchase is an idempotent replay
/// or a conflict. A replay requires the same plan and a matching, explicit idempotency key —
/// without a key we cannot prove intent, so the "one purchase per biller" rule wins with a 409.
/// </summary>
internal static class PurchaseRetryPolicy
{
    public static void EnsureIdempotentReplay(
        string billerId,
        PurchasePlan existingPlan,
        string? existingKey,
        PurchasePlan plan,
        string? idempotencyKey)
    {
        if (existingPlan != plan)
        {
            throw ServiceException.Conflict(
                "purchase_request_conflict",
                $"biller {billerId} already has a purchase with a different plan");
        }

        if (existingKey is not null && idempotencyKey is not null
            && string.Equals(existingKey, idempotencyKey, StringComparison.Ordinal))
        {
            return;
        }

        if (existingKey is not null && idempotencyKey is not null)
        {
            throw ServiceException.Conflict(
                "idempotency_key_conflict",
                $"biller {billerId} already has a purchase with a different idempotency key");
        }

        throw ServiceException.Conflict(
            "already_purchased", $"biller {billerId} already purchased the platform");
    }
}
