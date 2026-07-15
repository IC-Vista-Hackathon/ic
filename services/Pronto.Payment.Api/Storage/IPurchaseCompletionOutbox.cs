using Pronto.Payment.Api.Purchases;
using Pronto.Payment.Contracts.V1.Purchases;

namespace Pronto.Payment.Api.Storage;

/// <summary>
/// Durable retry queue for the Purchase-to-BillerAccount completion handoff. Implementations
/// persist the completion intent atomically with the pending purchase, retain it on failure, and
/// remove it atomically when the purchase becomes paid.
/// </summary>
public interface IPurchaseCompletionOutbox
{
    Task<IReadOnlyList<PurchaseCompletion>> ListPendingCompletionsAsync(
        int maxCount,
        CancellationToken cancellationToken = default);

    Task<PurchaseResponse?> CompleteAsync(
        PurchaseCompletion completion,
        CancellationToken cancellationToken = default);

    Task RecordCompletionFailureAsync(
        PurchaseCompletion completion,
        string failureReason,
        CancellationToken cancellationToken = default);
}
