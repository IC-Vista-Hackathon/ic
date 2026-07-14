using System.Collections.Concurrent;
using IC.Invoice.Contracts.V1.Invoices;
using IC.Payment.Api.Clients;
using IC.ServiceDefaults.Errors;

namespace IC.Payment.Api.Tests;

/// <summary>In-process stand-in for the Invoice Service with the same transition rules.</summary>
public sealed class FakeInvoiceClient : IInvoiceClient
{
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
