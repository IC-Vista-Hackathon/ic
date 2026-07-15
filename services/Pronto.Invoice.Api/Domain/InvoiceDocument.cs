namespace Pronto.Invoice.Api.Domain;

/// <summary>
/// A fake-seeded bill, queried by biller + account number.
/// Cosmos container <c>invoices</c>, partition key <c>/biller_id</c> (see design/entities.md).
/// Money is integer cents; <see cref="DueDate"/> is a plain date.
/// </summary>
/// <remarks>
/// Named <c>InvoiceDocument</c> (not <c>Invoice</c>) to avoid clashing with the
/// <c>Pronto.Invoice</c> namespace segment; the wire shape is <c>InvoiceResponse</c>.
/// </remarks>
public sealed class InvoiceDocument
{
    /// <summary>Cosmos-generated document id (GUID string).</summary>
    public required string Id { get; init; }

    /// <summary>Denormalized BillerAccount id; the partition key.</summary>
    public required string BillerId { get; init; }

    /// <summary>External account identifier — not a reference.</summary>
    public required string AccountNumber { get; init; }

    public required string PayerName { get; init; }

    public required string Description { get; init; }

    public required int AmountCents { get; init; }

    public required DateOnly DueDate { get; init; }

    public required InvoiceStatus Status { get; init; }

    /// <summary>Demo presentation hint — invoice type label (e.g. "Auto", "HOA Dues"); null shows none.</summary>
    public string? Type { get; init; }

    /// <summary>Demo presentation hint — status color badge: "green" | "yellow"; null shows none.</summary>
    public string? StatusColor { get; init; }

    /// <summary>Demo presentation hint — free-text note shown under the bill; null shows none.</summary>
    public string? Note { get; init; }

    /// <summary>Demo presentation hint — render <see cref="Note"/> emphasized (bold).</summary>
    public bool NoteEmphasis { get; init; }

    /// <summary>Id of the payment that produced the current status; null while due.</summary>
    public string? LastPaymentId { get; init; }
}
