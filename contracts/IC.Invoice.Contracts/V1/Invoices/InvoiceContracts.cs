namespace IC.Invoice.Contracts.V1.Invoices;

/// <summary>
/// Body for <c>POST /billers/{id}/invoices/seed</c> (internal fake-data seed at onboarding).
/// All fields optional — the service seeds a sensible default demo set when omitted.
/// </summary>
public sealed record SeedInvoicesRequest(
    int? Count = null,
    string? AccountNumber = null,
    string? BillType = null);

/// <summary>Result of a seed run — echoes what was generated so the caller can preview it.</summary>
public sealed record SeedInvoicesResponse(
    int Seeded,
    string AccountNumber,
    IReadOnlyList<InvoiceResponse> Invoices);

/// <summary>
/// Wire shape of an invoice. Money is integer cents; <see cref="DueDate"/> is a plain date;
/// <see cref="Status"/> is one of <c>due</c> | <c>scheduled</c> | <c>paid</c>
/// (see design/entities.md Invoice).
/// </summary>
public sealed record InvoiceResponse(
    string Id,
    string BillerId,
    string AccountNumber,
    string PayerName,
    string Description,
    int AmountCents,
    DateOnly DueDate,
    string Status);
