using Pronto.Payment.Api.Purchases;
using Pronto.Payment.Contracts.V1.Purchases;
using Pronto.ServiceDefaults.Errors;

namespace Pronto.Payment.Api.Storage;

public sealed class InMemoryPurchaseStore : IPurchaseStore, IPurchaseCompletionOutbox
{
    private readonly object gate = new();
    private readonly Dictionary<string, PurchaseState> purchases = [];

    public Task<PurchaseCreateResult> CreatePendingAsync(
        CreatePurchaseRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedKey = NormalizeIdempotencyKey(request.IdempotencyKey);

        lock (gate)
        {
            if (purchases.TryGetValue(request.BillerId, out var existing))
            {
                EnsureIdempotentReplay(
                    existing.Purchase.BillerId, existing.Purchase.Plan, existing.IdempotencyKey,
                    request.Plan, normalizedKey);
                return Task.FromResult(new PurchaseCreateResult(existing.Purchase, AlreadyExisted: true));
            }

            var purchase = new PurchaseResponse(
                PurchaseIdentity.ForBiller(request.BillerId),
                request.BillerId,
                request.Plan,
                PurchasePricing.AmountFor(request.Plan),
                PurchaseStatus.Pending);
            purchases.Add(
                request.BillerId,
                new PurchaseState(
                    purchase,
                    normalizedKey,
                    new PurchaseCompletion(request.BillerId, purchase.PurchaseId, request.Plan, Attempts: 0),
                    LastError: null));
            return Task.FromResult(new PurchaseCreateResult(purchase, AlreadyExisted: false));
        }
    }

    public Task<PurchaseResponse?> FindAsync(
        string billerId,
        string purchaseId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            var purchase = purchases.TryGetValue(billerId, out var state)
                && state.Purchase.PurchaseId == purchaseId
                ? state.Purchase
                : null;
            return Task.FromResult(purchase);
        }
    }

    public Task<IReadOnlyList<PurchaseCompletion>> ListPendingCompletionsAsync(
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            IReadOnlyList<PurchaseCompletion> completions = purchases.Values
                .Where(state => state.Completion is not null)
                .Select(state => state.Completion!)
                .Take(maxCount)
                .ToArray();
            return Task.FromResult(completions);
        }
    }

    public Task<PurchaseResponse?> CompleteAsync(
        PurchaseCompletion completion,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!purchases.TryGetValue(completion.BillerId, out var state)
                || state.Purchase.PurchaseId != completion.PurchaseId)
            {
                return Task.FromResult<PurchaseResponse?>(null);
            }

            var paid = state.Purchase.Status == PurchaseStatus.Paid
                ? state.Purchase
                : state.Purchase with { Status = PurchaseStatus.Paid };
            purchases[completion.BillerId] = state with
            {
                Purchase = paid,
                Completion = null,
                LastError = null,
            };
            return Task.FromResult<PurchaseResponse?>(paid);
        }
    }

    public Task RecordCompletionFailureAsync(
        PurchaseCompletion completion,
        string failureReason,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (purchases.TryGetValue(completion.BillerId, out var state)
                && state.Completion is not null
                && state.Purchase.PurchaseId == completion.PurchaseId)
            {
                purchases[completion.BillerId] = state with
                {
                    Completion = state.Completion with { Attempts = state.Completion.Attempts + 1 },
                    LastError = failureReason,
                };
            }
        }

        return Task.CompletedTask;
    }

    public Task PurgeByBillerAsync(string billerId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            purchases.Remove(billerId);
        }

        return Task.CompletedTask;
    }

    private static string? NormalizeIdempotencyKey(string? idempotencyKey) =>
        string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();

    private static void EnsureIdempotentReplay(
        string billerId,
        PurchasePlan existingPlan,
        string? existingKey,
        PurchasePlan plan,
        string? idempotencyKey)
        => PurchaseRetryPolicy.EnsureIdempotentReplay(billerId, existingPlan, existingKey, plan, idempotencyKey);

    private sealed record PurchaseState(
        PurchaseResponse Purchase,
        string? IdempotencyKey,
        PurchaseCompletion? Completion,
        string? LastError);
}
