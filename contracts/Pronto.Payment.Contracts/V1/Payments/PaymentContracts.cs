using System.Text.Json.Serialization;

namespace Pronto.Payment.Contracts.V1.Payments;

/// <summary>
/// Money-moving request: unknown members are rejected (<see cref="JsonUnmappedMemberHandling.Disallow"/>)
/// so a misspelled scheduling field cannot silently become an immediate payment.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
/// <param name="IdempotencyKey">
/// Optional durable client idempotency key. Retrying a create with the same key (and biller)
/// returns the original payment instead of creating a duplicate — safe across process restarts.
/// May also be supplied via the <c>Idempotency-Key</c> header, which takes precedence.
/// </param>
/// <param name="AmountCents">
/// Optional requested payment amount for a partial payment. Omitted (or null) pays the full
/// outstanding balance. The server is authoritative: it validates any supplied amount against the
/// balance it looks up (min-allowed &lt; amount ≤ outstanding) and never trusts it blindly.
/// </param>
/// <param name="InstallmentCount">
/// Optional number of installments to enroll in a payment plan (≥ 2). Omitted (or null) is a
/// one-time payment. The server validates the plan is permitted by the biller's policy.
/// </param>
public sealed record CreatePaymentRequest(
    string BillerId,
    string InvoiceId,
    string Method,
    string? PayerAccountId = null,
    DateOnly? ScheduledFor = null,
    string? IdempotencyKey = null,
    int? AmountCents = null,
    int? InstallmentCount = null);

/// <param name="InstallmentPlanId">
/// Set when this payment is one installment of an enrolled plan; groups the schedule's payments.
/// </param>
/// <param name="InstallmentSequence">Zero-based position of this installment within its plan.</param>
/// <param name="InstallmentCount">Total number of installments in this payment's plan.</param>
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
    DateTimeOffset CreatedAt,
    string? InstallmentPlanId = null,
    int? InstallmentSequence = null,
    int? InstallmentCount = null);

/// <summary>
/// The persisted installment schedule returned when a plan is enrolled: one entry per installment,
/// ordered by <see cref="PaymentResponse.InstallmentSequence"/>. Each entry is a scheduled payment
/// the processor settles on its <see cref="PaymentResponse.ScheduledFor"/> date.
/// </summary>
public sealed record InstallmentPlanResponse(
    string InstallmentPlanId,
    string BillerId,
    string InvoiceId,
    int InstallmentCount,
    int TotalAmountCents,
    IReadOnlyList<PaymentResponse> Installments);

/// <summary>
/// Server-side fee quote for <c>GET /payments/quote</c>. The PWA shows these numbers before
/// confirmation; the subsequent payment computes them identically, so what the payer approves
/// is what they are charged. <see cref="OutstandingCents"/> is the balance the server will accept a
/// payment up to; a partial-amount quote (<c>amount_cents</c>) prices the fee on that amount.
/// </summary>
public sealed record PaymentQuoteResponse(
    string BillerId,
    string InvoiceId,
    string Method,
    int AmountCents,
    int FeeCents,
    int TotalCents,
    int OutstandingCents = 0);

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
