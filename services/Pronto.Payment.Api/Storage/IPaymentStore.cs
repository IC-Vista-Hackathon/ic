using Pronto.Payment.Contracts.V1.Payments;

namespace Pronto.Payment.Api.Storage;

/// <summary>Outcome of a persist-before-mark create: the stored payment plus whether an
/// idempotency key made this an exact replay of an earlier request.</summary>
public readonly record struct PaymentCreation(PaymentResponse Payment, bool IsReplay);

public interface IPaymentStore
{
    /// <summary>
    /// Durably persist <paramref name="payment"/> (expected to be <see cref="PaymentStatus.Pending"/>)
    /// BEFORE the invoice transition is asserted. When <paramref name="idempotencyKey"/> is supplied
    /// it is reserved partition-scoped (conditional create); a repeat with the same key returns the
    /// original payment with <see cref="PaymentCreation.IsReplay"/> true instead of persisting again.
    /// </summary>
    Task<PaymentCreation> CreatePendingAsync(
        PaymentResponse payment, string? idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Replace an existing payment (e.g. Pending -> Succeeded/Scheduled).</summary>
    Task UpdateAsync(PaymentResponse payment, CancellationToken cancellationToken = default);

    /// <summary>The payment originally created under <paramref name="idempotencyKey"/>, if any.</summary>
    Task<PaymentResponse?> FindByIdempotencyKeyAsync(
        string billerId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<PaymentResponse?> FindAsync(string billerId, string paymentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PaymentResponse>> ListAsync(
        string billerId, string? payerAccountId, string? invoiceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Scheduled payments whose <c>ScheduledFor</c> is on or before <paramref name="asOf"/>, across all
    /// billers — drives the scheduled executor. Each result still carries its <c>biller_id</c> so the
    /// executor's follow-up work (invoice transition, status mark) stays partition-scoped.
    /// </summary>
    Task<IReadOnlyList<PaymentResponse>> ListDueScheduledAsync(
        DateOnly asOf, CancellationToken cancellationToken = default);

    /// <summary>Delete all payments in a biller's partition (nonprod test-cleanup only).</summary>
    Task PurgeByBillerAsync(string billerId, CancellationToken cancellationToken = default);
}

public sealed class InMemoryPaymentStore : IPaymentStore
{
    private readonly object gate = new();
    private readonly Dictionary<(string BillerId, string PaymentId), PaymentResponse> payments = [];
    private readonly Dictionary<(string BillerId, string Key), string> idempotency = [];

    public Task<PaymentCreation> CreatePendingAsync(
        PaymentResponse payment, string? idempotencyKey, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (idempotencyKey is not null
                && idempotency.TryGetValue((payment.BillerId, idempotencyKey), out var existingId)
                && payments.TryGetValue((payment.BillerId, existingId), out var existing))
            {
                return Task.FromResult(new PaymentCreation(existing, IsReplay: true));
            }

            payments[(payment.BillerId, payment.PaymentId)] = payment;
            if (idempotencyKey is not null)
            {
                idempotency[(payment.BillerId, idempotencyKey)] = payment.PaymentId;
            }

            return Task.FromResult(new PaymentCreation(payment, IsReplay: false));
        }
    }

    public Task UpdateAsync(PaymentResponse payment, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            payments[(payment.BillerId, payment.PaymentId)] = payment;
        }

        return Task.CompletedTask;
    }

    public Task<PaymentResponse?> FindByIdempotencyKeyAsync(
        string billerId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (idempotency.TryGetValue((billerId, idempotencyKey), out var paymentId))
            {
                return Task.FromResult(payments.GetValueOrDefault((billerId, paymentId)));
            }

            return Task.FromResult<PaymentResponse?>(null);
        }
    }

    public Task<PaymentResponse?> FindAsync(
        string billerId, string paymentId, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            return Task.FromResult(payments.GetValueOrDefault((billerId, paymentId)));
        }
    }

    public Task<IReadOnlyList<PaymentResponse>> ListAsync(
        string billerId, string? payerAccountId, string? invoiceId, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            IReadOnlyList<PaymentResponse> results = payments.Values
                .Where(payment => payment.BillerId == billerId
                    && (payerAccountId is null || payment.PayerAccountId == payerAccountId)
                    && (invoiceId is null || payment.InvoiceId == invoiceId))
                .OrderByDescending(payment => payment.CreatedAt)
                .ToArray();
            return Task.FromResult(results);
        }
    }

    public Task<IReadOnlyList<PaymentResponse>> ListDueScheduledAsync(
        DateOnly asOf, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            IReadOnlyList<PaymentResponse> results = payments.Values
                .Where(payment => payment.Status == PaymentStatus.Scheduled
                    && payment.ScheduledFor is not null
                    && payment.ScheduledFor.Value <= asOf)
                .OrderBy(payment => payment.ScheduledFor)
                .ToArray();
            return Task.FromResult(results);
        }
    }

    public Task PurgeByBillerAsync(string billerId, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            foreach (var key in payments.Keys.Where(k => k.BillerId == billerId).ToList())
            {
                payments.Remove(key);
            }

            foreach (var key in idempotency.Keys.Where(k => k.BillerId == billerId).ToList())
            {
                idempotency.Remove(key);
            }
        }

        return Task.CompletedTask;
    }
}
