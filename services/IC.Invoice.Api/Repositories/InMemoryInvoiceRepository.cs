using System.Collections.Concurrent;
using IC.Invoice.Api.Domain;

namespace IC.Invoice.Api.Repositories;

/// <summary>
/// Process-local invoice store. Keyed by biller_id to mirror the Cosmos
/// partition boundary, so query semantics match the eventual SDK implementation.
/// </summary>
public sealed class InMemoryInvoiceRepository : IInvoiceRepository
{
    private readonly ConcurrentDictionary<string, List<InvoiceDocument>> _byBiller = new(StringComparer.Ordinal);

    public Task AddRangeAsync(IEnumerable<InvoiceDocument> invoices, CancellationToken cancellationToken = default)
    {
        foreach (var invoice in invoices)
        {
            var partition = _byBiller.GetOrAdd(invoice.BillerId, static _ => new List<InvoiceDocument>());
            lock (partition)
            {
                partition.Add(invoice);
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<InvoiceDocument>> GetOpenAsync(
        string billerId,
        string accountNumber,
        CancellationToken cancellationToken = default)
    {
        if (!_byBiller.TryGetValue(billerId, out var partition))
        {
            return Task.FromResult<IReadOnlyList<InvoiceDocument>>(Array.Empty<InvoiceDocument>());
        }

        List<InvoiceDocument> matches;
        lock (partition)
        {
            matches = partition
                .Where(i => string.Equals(i.AccountNumber, accountNumber, StringComparison.Ordinal)
                    && i.Status != InvoiceStatus.Paid)
                .ToList();
        }

        return Task.FromResult<IReadOnlyList<InvoiceDocument>>(matches);
    }
}
