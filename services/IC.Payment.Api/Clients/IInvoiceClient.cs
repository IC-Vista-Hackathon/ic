using IC.Invoice.Contracts.V1.Invoices;

namespace IC.Payment.Api.Clients;

public interface IInvoiceClient
{
    Task<InvoiceResponse?> GetAsync(string billerId, string invoiceId, CancellationToken cancellationToken);

    /// <summary>Asserts the transition on the Invoice Service; surfaces its 409 as ServiceException.</summary>
    Task<InvoiceResponse> UpdateStatusAsync(
        string billerId,
        string invoiceId,
        UpdateInvoiceStatusRequest request,
        CancellationToken cancellationToken);
}
