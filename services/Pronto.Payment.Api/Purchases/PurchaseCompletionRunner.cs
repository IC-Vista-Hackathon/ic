using Pronto.Payment.Api.Clients;
using Pronto.Payment.Api.Storage;
using Pronto.Payment.Contracts.V1.Purchases;
using Microsoft.Extensions.Logging;

namespace Pronto.Payment.Api.Purchases;

/// <summary>
/// Drives a single purchase from <c>pending</c> to <c>paid</c>: advances the downstream
/// BillerAccount and only then marks the purchase paid and clears its durable completion record.
/// A failed downstream call leaves the purchase pending and its outbox intact for retry, so a
/// purchase is never reported paid before the cross-service transition is committed.
/// </summary>
public sealed partial class PurchaseCompletionRunner(
    IPurchaseCompletionOutbox outbox,
    IBillerAccountClient billerAccounts,
    ILogger<PurchaseCompletionRunner> logger)
{
    /// <summary>
    /// Attempts to complete a pending purchase. Returns the paid purchase on success, or
    /// <see langword="null"/> when the downstream transition failed and it remains pending.
    /// </summary>
    public async Task<PurchaseResponse?> TryCompleteAsync(
        PurchaseCompletion completion,
        CancellationToken cancellationToken)
    {
        try
        {
            await billerAccounts
                .AdvanceToPurchasedAsync(
                    completion.BillerId, completion.Plan, completion.PurchaseId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogCompletionDeferred(logger, completion.BillerId, completion.PurchaseId, exception);
            await outbox
                .RecordCompletionFailureAsync(completion, exception.Message, cancellationToken)
                .ConfigureAwait(false);
            return null;
        }

        return await outbox.CompleteAsync(completion, cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(
        400,
        LogLevel.Warning,
        "Purchase {PurchaseId} for biller {BillerId} left pending; BillerAccount transition failed and remains queued for retry")]
    private static partial void LogCompletionDeferred(
        ILogger logger,
        string billerId,
        string purchaseId,
        Exception exception);
}
