namespace IC.Invoice.Contracts.V1.Invoices;

public sealed record InvoiceResponse(
    string InvoiceId,
    string BillerId,
    string AccountNumber,
    string PayerName,
    string Description,
    int AmountCents,
    DateOnly DueDate,
    InvoiceStatus Status);

public sealed record InvoiceListResponse(
    IReadOnlyList<InvoiceResponse> Invoices);

public sealed record SeedInvoicesRequest(
    int Count,
    string? AccountNumber = null,
    string? PayerName = null);

public sealed record UpdateInvoiceStatusRequest(
    InvoiceStatus Status,
    string PaymentId);

public enum InvoiceStatus
{
    Due,
    Scheduled,
    Paid
}
