namespace IC.Invoice.Api.Domain;

/// <summary>Maps the <see cref="InvoiceStatus"/> enum to its lowercase wire token.</summary>
public static class InvoiceStatusWire
{
    public static string ToWire(this InvoiceStatus status) => status switch
    {
        InvoiceStatus.Due => "due",
        InvoiceStatus.Scheduled => "scheduled",
        InvoiceStatus.Paid => "paid",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown invoice status."),
    };
}
