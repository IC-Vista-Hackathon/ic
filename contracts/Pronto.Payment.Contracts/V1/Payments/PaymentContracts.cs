using System.Text.Json.Serialization;

namespace Pronto.Payment.Contracts.V1.Payments;

public sealed record CreatePaymentRequest(
    string BillerId,
    string InvoiceId,
    string Method,
    string? PayerAccountId = null,
    DateOnly? ScheduledFor = null);

public sealed record PaymentResponse(
    string PaymentId,
    string BillerId,
    string InvoiceId,
    string? PayerAccountId,
    string Method,
    int AmountCents,
    int FeeCents,
    int TotalCents,
    string Confirmation,
    PaymentStatus Status,
    DateOnly? ScheduledFor,
    string ReceiptMessage,
    DateTimeOffset CreatedAt);

/// <summary>
/// Server-side fee quote for <c>GET /payments/quote</c>. The PWA shows these numbers before
/// confirmation; the subsequent payment computes them identically, so what the payer approves
/// is what they are charged.
/// </summary>
public sealed record PaymentQuoteResponse(
    string BillerId,
    string InvoiceId,
    string Method,
    int AmountCents,
    int FeeCents,
    int TotalCents);

/// <summary>Wire tokens pinned at the type level so serialization is host-independent.</summary>
[JsonConverter(typeof(PaymentStatusJsonConverter))]
public enum PaymentStatus
{
    /// <summary>
    /// Durably persisted before the invoice transition is asserted. A crash after this point
    /// but before <see cref="Succeeded"/>/<see cref="Scheduled"/> leaves a recoverable,
    /// auditable payment row rather than an orphaned invoice with no payment record.
    /// </summary>
    [JsonStringEnumMemberName("pending")]
    Pending,

    [JsonStringEnumMemberName("scheduled")]
    Scheduled,

    [JsonStringEnumMemberName("succeeded")]
    Succeeded,

    [JsonStringEnumMemberName("failed")]
    Failed,
}

/// <summary>String-only converter for <see cref="PaymentStatus"/> (rejects integer tokens).</summary>
public sealed class PaymentStatusJsonConverter : JsonStringEnumConverter<PaymentStatus>
{
    public PaymentStatusJsonConverter()
        : base(namingPolicy: null, allowIntegerValues: false)
    {
    }
}
