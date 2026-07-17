using Pronto.Invoice.Api.Domain;

namespace Pronto.Invoice.Api.Repositories;

/// <summary>
/// Persistence boundary for invoices. Backed by an in-memory store for now;
/// a Cosmos DB implementation swaps in later without touching callers.
/// All reads are partition-scoped by <c>biller_id</c>.
/// </summary>
public interface IInvoiceRepository
{
    /// <summary>Persist a batch of invoices (one biller's seed set).</summary>
    Task AddRangeAsync(IEnumerable<InvoiceDocument> invoices, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replace the full set of invoices for a single <paramref name="billerId"/> +
    /// <paramref name="accountNumber"/> with <paramref name="invoices"/>: the new set is upserted
    /// and any prior invoice for that account that is not in the new set is removed. Used by the
    /// onboarding seed so re-publishing reflects only the latest profile — including when the new
    /// profile produces <em>fewer</em> invoices than before (slot-based upsert alone would leave
    /// the shrunk-away slots orphaned). Other accounts in the partition are untouched.
    /// </summary>
    Task ReplaceAccountAsync(
        string billerId,
        string accountNumber,
        IReadOnlyList<InvoiceDocument> invoices,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Open (non-paid) invoices for a biller + account number, within one partition.
    /// </summary>
    Task<IReadOnlyList<InvoiceDocument>> GetOpenAsync(
        string billerId,
        string accountNumber,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InvoiceDocument>> GetByAccountAsync(
        string billerId,
        string accountNumber,
        CancellationToken cancellationToken = default);

    /// <summary>Point read within one partition; null when absent.</summary>
    Task<InvoiceDocument?> FindAsync(
        string billerId,
        string invoiceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomic conditional transition (<c>due→paid</c>, <c>due→scheduled</c>, <c>scheduled→paid</c>),
    /// idempotent when the invoice already carries <paramref name="paymentId"/> in the target
    /// status. A <c>scheduled</c> invoice is bound to the payment that scheduled it: only that
    /// same <paramref name="paymentId"/> may settle it (<c>scheduled→paid</c>) — any other payment
    /// yields <see cref="InvoiceTransitionOutcome.ScheduleLocked"/> so a second payment cannot
    /// take over or double-settle an active scheduled invoice. Outcomes: the updated document, or
    /// a conflict reason.
    /// </summary>
    Task<InvoiceTransitionResult> TryUpdateStatusAsync(
        string billerId,
        string invoiceId,
        InvoiceStatus target,
        string paymentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete every invoice in a biller's partition. Test-cleanup support for functional
    /// tests against a shared store; exposed only through the nonprod-gated maintenance endpoint.
    /// </summary>
    Task PurgeByBillerAsync(string billerId, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a conditional status transition.</summary>
public sealed record InvoiceTransitionResult(
    InvoiceTransitionOutcome Outcome,
    InvoiceDocument? Invoice);

public enum InvoiceTransitionOutcome
{
    Updated,
    NotFound,
    AlreadyPaid,
    InvalidTransition,

    /// <summary>
    /// The invoice is <c>scheduled</c> against a different payment than the one asserting the
    /// transition. The scheduled state is bound to its originating payment, so no other payment
    /// may settle or re-schedule it.
    /// </summary>
    ScheduleLocked,
}
