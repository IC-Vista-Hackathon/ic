using System.Collections.Concurrent;
using Pronto.Invoice.Api.Domain;

namespace Pronto.Invoice.Api.Repositories;

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

    public Task PurgeByBillerAsync(string billerId, CancellationToken cancellationToken = default)
    {
        _byBiller.TryRemove(billerId, out _);
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

    public Task<IReadOnlyList<InvoiceDocument>> GetByAccountAsync(
        string billerId,
        string accountNumber,
        CancellationToken cancellationToken = default)
    {
        if (!_byBiller.TryGetValue(billerId, out var partition))
        {
            return Task.FromResult<IReadOnlyList<InvoiceDocument>>(Array.Empty<InvoiceDocument>());
        }
        lock (partition)
        {
            return Task.FromResult<IReadOnlyList<InvoiceDocument>>(partition
                .Where(invoice => string.Equals(invoice.AccountNumber, accountNumber, StringComparison.Ordinal))
                .OrderByDescending(invoice => invoice.DueDate)
                .ToArray());
        }
    }

    public Task<InvoiceDocument?> FindAsync(
        string billerId,
        string invoiceId,
        CancellationToken cancellationToken = default)
    {
        if (!_byBiller.TryGetValue(billerId, out var partition))
        {
            return Task.FromResult<InvoiceDocument?>(null);
        }

        lock (partition)
        {
            return Task.FromResult(partition.FirstOrDefault(
                i => string.Equals(i.Id, invoiceId, StringComparison.Ordinal)));
        }
    }

    public Task<InvoiceTransitionResult> TryUpdateStatusAsync(
        string billerId,
        string invoiceId,
        InvoiceStatus target,
        string paymentId,
        CancellationToken cancellationToken = default)
    {
        if (!_byBiller.TryGetValue(billerId, out var partition))
        {
            return Task.FromResult(new InvoiceTransitionResult(InvoiceTransitionOutcome.NotFound, null));
        }

        lock (partition)
        {
            var index = partition.FindIndex(
                i => string.Equals(i.Id, invoiceId, StringComparison.Ordinal));
            if (index < 0)
            {
                return Task.FromResult(new InvoiceTransitionResult(InvoiceTransitionOutcome.NotFound, null));
            }

            var invoice = partition[index];

            var decision = InvoiceTransitionRules.Decide(
                invoice.Status, invoice.LastPaymentId, target, paymentId);
            if (decision != TransitionDecision.Apply)
            {
                return Task.FromResult(new InvoiceTransitionResult(decision.ToOutcome(), invoice));
            }

            var updated = new InvoiceDocument
            {
                Id = invoice.Id,
                BillerId = invoice.BillerId,
                AccountNumber = invoice.AccountNumber,
                PayerName = invoice.PayerName,
                Description = invoice.Description,
                AmountCents = invoice.AmountCents,
                DueDate = invoice.DueDate,
                Status = target,
                LastPaymentId = paymentId,
            };
            partition[index] = updated;
            return Task.FromResult(new InvoiceTransitionResult(InvoiceTransitionOutcome.Updated, updated));
        }
    }
}
