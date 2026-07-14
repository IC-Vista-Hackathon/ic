using System.Collections.Concurrent;
using Pronto.Invoice.Contracts.V1.Invoices;
using Pronto.Payment.Api.Clients;
using Pronto.ServiceDefaults.Errors;

namespace Pronto.Payment.Api.Tests;

/// <summary>In-process stand-in for the Invoice Service with the same transition rules.</summary>
public sealed class FakeInvoiceClient : IInvoiceClient
{
    private readonly object gate = new();
    private readonly ConcurrentDictionary<(string BillerId, string InvoiceId), InvoiceResponse> invoices = new();

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
        invoices[(billerId, invoice.Id)] = invoice;
        return invoice;
    }

    public Task<InvoiceResponse?> GetAsync(
        string billerId, string invoiceId, CancellationToken cancellationToken)
        => Task.FromResult(invoices.GetValueOrDefault((billerId, invoiceId)));

    public Task<InvoiceResponse> UpdateStatusAsync(
        string billerId,
        string invoiceId,
        UpdateInvoiceStatusRequest request,
        CancellationToken cancellationToken)
    {
        // Check-and-set under one lock, matching the real repository's atomicity guarantee.
        lock (gate)
        {
            var key = (billerId, invoiceId);
            var invoice = invoices.GetValueOrDefault(key)
                ?? throw ServiceException.NotFound("not_found", $"invoice {invoiceId} not found");

            if (invoice.Status == InvoiceStatus.Paid)
            {
                throw ServiceException.Conflict("already_paid", $"invoice {invoiceId} is already paid");
            }

            var updated = invoice with { Status = request.Status };
            invoices[key] = updated;
            return Task.FromResult(updated);
        }
    }
}
