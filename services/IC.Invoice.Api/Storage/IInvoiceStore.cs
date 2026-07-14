using IC.Invoice.Contracts.V1.Invoices;

namespace IC.Invoice.Api.Storage;

public interface IInvoiceStore
{
    IReadOnlyList<InvoiceResponse> List(string billerId, string? accountNumber, InvoiceStatus? status);

    InvoiceResponse? Find(string billerId, string invoiceId);

    IReadOnlyList<InvoiceResponse> Seed(string billerId, SeedInvoicesRequest request);

    /// <summary>
    /// Conditional transition: due→paid, due→scheduled, scheduled→paid. Idempotent when the
    /// invoice is already in the target status for the same payment. Throws ServiceException
    /// (409) otherwise. Single lock makes check-and-set atomic.
    /// </summary>
    InvoiceResponse UpdateStatus(string billerId, string invoiceId, UpdateInvoiceStatusRequest request);
}
