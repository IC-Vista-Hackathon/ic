using WireInvoiceStatus = IC.Invoice.Contracts.V1.Invoices.InvoiceStatus;

namespace IC.Invoice.Api.Domain;

/// <summary>Maps the domain <see cref="InvoiceStatus"/> to/from the contract enum.</summary>
public static class InvoiceStatusWire
{
    public static WireInvoiceStatus ToWire(this InvoiceStatus status) => status switch
    {
        InvoiceStatus.Due => WireInvoiceStatus.Due,
        InvoiceStatus.Scheduled => WireInvoiceStatus.Scheduled,
        InvoiceStatus.Paid => WireInvoiceStatus.Paid,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown invoice status."),
    };

    public static InvoiceStatus ToDomain(this WireInvoiceStatus status) => status switch
    {
        WireInvoiceStatus.Due => InvoiceStatus.Due,
        WireInvoiceStatus.Scheduled => InvoiceStatus.Scheduled,
        WireInvoiceStatus.Paid => InvoiceStatus.Paid,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown invoice status."),
    };
}
