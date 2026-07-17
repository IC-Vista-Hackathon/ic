using System.Text.Json.Serialization;

namespace Pronto.Invoice.Contracts.V1.Invoices;

/// <summary>
/// Invoice lifecycle status. Wire tokens are the lowercase strings shown in
/// design/entities.md (<c>due</c> | <c>scheduled</c> | <c>paid</c>) — the converter is attached
/// at the type level, so every host serializes them identically regardless of its JSON options.
/// Integer tokens are rejected on the wire.
/// </summary>
[JsonConverter(typeof(InvoiceStatusJsonConverter))]
public enum InvoiceStatus
{
    [JsonStringEnumMemberName("due")]
    Due,

    [JsonStringEnumMemberName("scheduled")]
    Scheduled,

    [JsonStringEnumMemberName("paid")]
    Paid,
}

/// <summary>String-only converter for <see cref="InvoiceStatus"/> (rejects integer tokens).</summary>
public sealed class InvoiceStatusJsonConverter : JsonStringEnumConverter<InvoiceStatus>
{
    public InvoiceStatusJsonConverter()
        : base(namingPolicy: null, allowIntegerValues: false)
    {
    }
}

/// <summary>
/// Body for <c>POST /billers/{id}/invoices/seed</c> (internal fake-data seed at onboarding).
/// All fields optional — the service seeds a sensible default demo set when omitted.
/// </summary>
/// <remarks>
/// Per "agents configure, deterministic services execute": the caller (Biller Experience side,
/// which owns onboarding and the biller profile) may supply <see cref="Invoices"/> — pre-generated,
/// biller-relevant demo line items — and the Invoice service persists them as-is. When
/// <see cref="Invoices"/> is omitted the service falls back to a generic index-driven demo set
/// (optionally themed by <see cref="BillType"/>). The Invoice service never hand-authors a fixed
/// set keyed on <see cref="BillType"/>.
/// </remarks>
public sealed record SeedInvoicesRequest(
    int? Count = null,
    string? AccountNumber = null,
    string? BillType = null,
    IReadOnlyList<SeedInvoiceSpec>? Invoices = null);

/// <summary>
/// A caller-supplied demo invoice line item. The caller chooses biller-relevant content; the
/// Invoice service stamps biller/account scoping, a lifecycle status of <c>due</c>, and anchors
/// <see cref="DueInDays"/> to its own clock. Money is integer cents; <see cref="DueInDays"/> is a
/// relative offset from "today" so seeded due dates stay stable regardless of when the seed runs.
/// <see cref="Type"/>, <see cref="StatusColor"/>, <see cref="Note"/>, and <see cref="NoteEmphasis"/>
/// are optional demo presentation hints layered on top of the payment lifecycle status.
/// </summary>
public sealed record SeedInvoiceSpec(
    string Description,
    int AmountCents,
    int DueInDays,
    string? PayerName = null,
    string? Type = null,
    string? StatusColor = null,
    string? Note = null,
    bool NoteEmphasis = false);

/// <summary>Result of a seed run — echoes what was generated so the caller can preview it.</summary>
public sealed record SeedInvoicesResponse(
    int Seeded,
    string AccountNumber,
    IReadOnlyList<InvoiceResponse> Invoices);

/// <summary>
/// Wire shape of an invoice. Money is integer cents; <see cref="DueDate"/> is a plain date;
/// <see cref="Status"/> serializes as its lowercase token (see design/entities.md Invoice).
/// <see cref="Type"/>, <see cref="StatusColor"/>, <see cref="Note"/>, and
/// <see cref="NoteEmphasis"/> are demo presentation hints (optional) layered on top of the
/// payment lifecycle <see cref="Status"/> — they don't affect money movement.
/// </summary>
public sealed record InvoiceResponse(
    string Id,
    string BillerId,
    string AccountNumber,
    string PayerName,
    string Description,
    int AmountCents,
    DateOnly DueDate,
    InvoiceStatus Status,
    string? Type = null,
    string? StatusColor = null,
    string? Note = null,
    bool NoteEmphasis = false);

/// <summary>Result of <c>GET /billers/{id}/invoices</c> — open invoices unless filtered.</summary>
public sealed record InvoiceListResponse(
    IReadOnlyList<InvoiceResponse> Invoices);

/// <summary>
/// Body for <c>POST /billers/{id}/invoices/{invoiceId}/status</c> (internal — Payment Service
/// asserts <c>due→paid</c>, <c>due→scheduled</c>, or <c>scheduled→paid</c>).
/// <see cref="PaymentId"/> makes the transition idempotent per payment.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateInvoiceStatusRequest(
    InvoiceStatus Status,
    string PaymentId);
