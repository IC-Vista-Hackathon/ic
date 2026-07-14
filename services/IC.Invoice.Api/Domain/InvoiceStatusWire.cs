namespace IC.Invoice.Api.Domain;

/// <summary>Maps the <see cref="InvoiceStatus"/> enum to/from its lowercase wire token.</summary>
public static class InvoiceStatusWire
{
    public static string ToWire(this InvoiceStatus status) => status switch
    {
        InvoiceStatus.Due => "due",
        InvoiceStatus.Scheduled => "scheduled",
        InvoiceStatus.Paid => "paid",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown invoice status."),
    };

    /// <summary>Parses a wire token; null when the token is not a known status.</summary>
    public static InvoiceStatus? FromWire(string? token) => token switch
    {
        "due" => InvoiceStatus.Due,
        "scheduled" => InvoiceStatus.Scheduled,
        "paid" => InvoiceStatus.Paid,
        _ => null,
    };
}
