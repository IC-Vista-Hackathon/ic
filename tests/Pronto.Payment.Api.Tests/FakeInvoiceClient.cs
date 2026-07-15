using Pronto.Invoice.Contracts.V1.Invoices;
using Pronto.Payment.Api.Clients;
using Pronto.ServiceDefaults.Errors;

namespace Pronto.Payment.Api.Tests;

/// <summary>
/// In-process stand-in for the Invoice Service that mirrors the real repository's transition
/// rules — including the scheduled→paid binding to the originating payment — plus failure/latency
/// hooks so the durable-workflow and recovery paths can be exercised deterministically.
/// </summary>
public sealed class FakeInvoiceClient : IInvoiceClient
{
    private readonly object gate = new();
    private readonly Dictionary<(string BillerId, string InvoiceId), Entry> invoices = new();
    private int updateStatusCalls;

    /// <summary>When set, invoked on every <see cref="UpdateStatusAsync"/> before applying it —
    /// throw from here to simulate an Invoice Service failure.</summary>
    public Action<string, string, UpdateInvoiceStatusRequest>? OnUpdateStatus { get; set; }

    /// <summary>Count of applied (successful) status transitions, for asserting idempotency.</summary>
    public int AppliedTransitions { get; private set; }

    public int UpdateStatusCalls => Volatile.Read(ref updateStatusCalls);

    public Exception? UpdateStatusFault { get; set; }

    public InvoiceResponse AddDueInvoice(string billerId, int amountCents)
    {
        var invoice = new InvoiceResponse(
            Id: Guid.NewGuid().ToString(),
            BillerId: billerId,
            AccountNumber: "ACCT-1",
            PayerName: "Test Payer",
            Description: "Test invoice",
            AmountCents: amountCents,
            DueDate: new DateOnly(2026, 7, 25),
            Status: InvoiceStatus.Due);

        lock (gate)
        {
            invoices[(billerId, invoice.Id)] = new Entry(invoice, LastPaymentId: null);
        }

        return invoice;
    }

    public InvoiceStatus StatusOf(string billerId, string invoiceId)
    {
        lock (gate)
        {
            return invoices[(billerId, invoiceId)].Invoice.Status;
        }
    }

    public string? LastPaymentIdOf(string billerId, string invoiceId)
    {
        lock (gate)
        {
            return invoices[(billerId, invoiceId)].LastPaymentId;
        }
    }

    public Task<InvoiceResponse?> GetAsync(
        string billerId, string invoiceId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(
                invoices.TryGetValue((billerId, invoiceId), out var entry) ? entry.Invoice : null);
        }
    }

    public Task<InvoiceResponse> UpdateStatusAsync(
        string billerId,
        string invoiceId,
        UpdateInvoiceStatusRequest request,
        CancellationToken cancellationToken)
    {
        OnUpdateStatus?.Invoke(billerId, invoiceId, request);

        // Check-and-set under one lock, matching the real repository's atomicity guarantee.
        lock (gate)
        {
            Interlocked.Increment(ref updateStatusCalls);
            if (UpdateStatusFault is { } fault)
            {
                UpdateStatusFault = null;
                throw fault;
            }

            var key = (billerId, invoiceId);
            if (!invoices.TryGetValue(key, out var entry))
            {
                throw ServiceException.NotFound("not_found", $"invoice {invoiceId} not found");
            }

            var invoice = entry.Invoice;
            var ownsCurrent = entry.LastPaymentId is not null
                && string.Equals(entry.LastPaymentId, request.PaymentId, StringComparison.Ordinal);

            if (invoice.Status == request.Status && ownsCurrent)
            {
                return Task.FromResult(invoice); // idempotent replay
            }

            if (invoice.Status == InvoiceStatus.Paid)
            {
                throw ServiceException.Conflict("already_paid", $"invoice {invoiceId} is already paid");
            }

            if (invoice.Status == InvoiceStatus.Scheduled && entry.LastPaymentId is not null && !ownsCurrent)
            {
                throw ServiceException.Conflict(
                    "schedule_locked", $"invoice {invoiceId} has an active scheduled payment");
            }

            var allowed = (invoice.Status, request.Status) switch
            {
                (InvoiceStatus.Due, InvoiceStatus.Paid) => true,
                (InvoiceStatus.Due, InvoiceStatus.Scheduled) => true,
                (InvoiceStatus.Scheduled, InvoiceStatus.Paid) => true,
                _ => false,
            };

            if (!allowed)
            {
                throw ServiceException.Conflict(
                    "invalid_transition", $"invoice {invoiceId} cannot move to {request.Status}");
            }

            var updated = invoice with { Status = request.Status };
            invoices[key] = new Entry(updated, request.PaymentId);
            AppliedTransitions++;
            return Task.FromResult(updated);
        }
    }

    private sealed record Entry(InvoiceResponse Invoice, string? LastPaymentId);
}
