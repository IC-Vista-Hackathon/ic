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
    /// status. Outcomes: the updated document, or a conflict reason.
    /// </summary>
    Task<InvoiceTransitionResult> TryUpdateStatusAsync(
        string billerId,
        string invoiceId,
        InvoiceStatus target,
        string paymentId,
        CancellationToken cancellationToken = default);
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
}
