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

/// <summary>Result of <c>GET /billers/{id}/invoices</c> — open invoices unless filtered.</summary>
public sealed record InvoiceListResponse(
    IReadOnlyList<InvoiceResponse> Invoices);

/// <summary>
/// Body for <c>POST /billers/{id}/invoices/{invoiceId}/status</c> (internal — Payment Service
/// asserts <c>due→paid</c>, <c>due→scheduled</c>, or <c>scheduled→paid</c>). <see cref="Status"/>
/// is the lowercase wire token; <see cref="PaymentId"/> makes the transition idempotent per payment.
/// </summary>
public sealed record UpdateInvoiceStatusRequest(
    string Status,
    string PaymentId);
