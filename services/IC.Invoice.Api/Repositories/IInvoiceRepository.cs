using IC.Invoice.Api.Domain;

namespace IC.Invoice.Api.Repositories;

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
}
