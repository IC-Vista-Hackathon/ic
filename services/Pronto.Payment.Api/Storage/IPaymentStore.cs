using Pronto.Payment.Api.Domain;

namespace Pronto.Payment.Api.Storage;

/// <summary>
/// Durable payment persistence. Beyond simple CRUD it exposes the operations the recoverable
/// payment workflow needs: an idempotent <see cref="BeginAsync"/> that atomically claims a
/// pending record (durable client idempotency), <see cref="SaveAsync"/> to finalize it, and
/// <see cref="ClaimDueAsync"/> for the scheduled-payment processor to lease due scheduled payments
/// and recover orphaned pending ones.
/// </summary>
public interface IPaymentStore
{
    /// <summary>
    /// Atomically insert <paramref name="pending"/>. If a record with the same id already exists
    /// (i.e. the same derived idempotency key), returns it with <c>Created=false</c> instead of
    /// inserting — so a retried client request never creates a duplicate payment.
    /// </summary>
    Task<PaymentBeginResult> BeginAsync(PaymentRecord pending, CancellationToken cancellationToken = default);

    /// <summary>Persist a lifecycle transition (finalization, failure, lease change).</summary>
    Task<PaymentRecord> SaveAsync(PaymentRecord record, CancellationToken cancellationToken = default);

    Task<PaymentRecord?> FindAsync(string billerId, string paymentId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaymentRecord>> ListAsync(
        string billerId, string? payerAccountId, string? invoiceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Claim (with an exclusive lease) one payment that needs the processor's attention: either a
    /// scheduled payment whose <c>scheduled_for</c> has arrived (<paramref name="asOf"/>), or a
    /// pending payment stranded since before <paramref name="staleBefore"/> (crash recovery). A
    /// record whose lease is still active (<c>LeaseUntil &gt; now</c>) is skipped. Returns null when
    /// nothing is claimable.
    /// </summary>
    Task<PaymentRecord?> ClaimDueAsync(
        DateOnly asOf,
        DateTimeOffset now,
        DateTimeOffset staleBefore,
        DateTimeOffset leaseUntil,
        CancellationToken cancellationToken = default);

    /// <summary>Delete all payments in a biller's partition (nonprod test-cleanup only).</summary>
    Task PurgeByBillerAsync(string billerId, CancellationToken cancellationToken = default);
}

public sealed class InMemoryPaymentStore : IPaymentStore
{
    private readonly object gate = new();
    private readonly Dictionary<(string BillerId, string PaymentId), PaymentRecord> payments = [];

    public Task<PaymentBeginResult> BeginAsync(
        PaymentRecord pending, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var key = (pending.BillerId, pending.PaymentId);
            if (payments.TryGetValue(key, out var existing))
            {
                return Task.FromResult(new PaymentBeginResult(Created: false, existing));
            }

            payments[key] = pending;
            return Task.FromResult(new PaymentBeginResult(Created: true, pending));
        }
    }

    public Task<PaymentRecord> SaveAsync(PaymentRecord record, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            payments[(record.BillerId, record.PaymentId)] = record;
        }

        return Task.FromResult(record);
    }

    public Task<PaymentRecord?> FindAsync(
        string billerId, string paymentId, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            return Task.FromResult(payments.GetValueOrDefault((billerId, paymentId)));
        }
    }

    public Task<IReadOnlyList<PaymentRecord>> ListAsync(
        string billerId, string? payerAccountId, string? invoiceId, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            IReadOnlyList<PaymentRecord> results = payments.Values
                .Where(payment => payment.BillerId == billerId
                    && payment.IsFinalized
                    && (payerAccountId is null || payment.PayerAccountId == payerAccountId)
                    && (invoiceId is null || payment.InvoiceId == invoiceId))
                .OrderByDescending(payment => payment.CreatedAt)
                .ToArray();
            return Task.FromResult(results);
        }
    }

    public Task<PaymentRecord?> ClaimDueAsync(
        DateOnly asOf,
        DateTimeOffset now,
        DateTimeOffset staleBefore,
        DateTimeOffset leaseUntil,
        CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            foreach (var key in payments.Keys.ToList())
            {
                var record = payments[key];
                var leaseAvailable = record.LeaseUntil is null || record.LeaseUntil <= now;
                if (!leaseAvailable || !IsDue(record, asOf, staleBefore))
                {
                    continue;
                }

                var claimed = record with { LeaseUntil = leaseUntil, UpdatedAt = now };
                payments[key] = claimed;
                return Task.FromResult<PaymentRecord?>(claimed);
            }

            return Task.FromResult<PaymentRecord?>(null);
        }
    }

    private static bool IsDue(PaymentRecord record, DateOnly asOf, DateTimeOffset staleBefore) =>
        (record.Lifecycle == PaymentLifecycle.Scheduled
            && record.ScheduledFor is { } scheduledFor && scheduledFor <= asOf)
        || (record.Lifecycle == PaymentLifecycle.Pending && record.UpdatedAt <= staleBefore);

    public Task PurgeByBillerAsync(string billerId, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            foreach (var key in payments.Keys.Where(k => k.BillerId == billerId).ToList())
            {
                payments.Remove(key);
            }
        }

        return Task.CompletedTask;
    }
}
