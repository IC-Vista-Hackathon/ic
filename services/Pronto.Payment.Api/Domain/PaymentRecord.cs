using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Pronto.Payment.Contracts.V1.Payments;

namespace Pronto.Payment.Api.Domain;

/// <summary>
/// Internal payment lifecycle. The wire <see cref="PaymentStatus"/> only distinguishes
/// terminal/settled states; this adds the <see cref="Pending"/> state that makes the
/// invoice-first orphan window recoverable: the payment row is written durably <em>before</em>
/// the invoice transition, so a crash between the two leaves a claimable pending record rather
/// than a paid invoice with no payment.
/// </summary>
public enum PaymentLifecycle
{
    /// <summary>Durably persisted, but the authoritative invoice transition is not yet confirmed.</summary>
    Pending,

    /// <summary>Invoice moved to <c>scheduled</c>; awaiting its <c>scheduled_for</c> date.</summary>
    Scheduled,

    /// <summary>Invoice settled to <c>paid</c>; the payment is complete.</summary>
    Succeeded,

    /// <summary>Abandoned — the invoice could not be transitioned for this payment.</summary>
    Failed,
}

/// <summary>
/// The persisted payment aggregate. Container <c>payments</c>, partition key <c>/biller_id</c>.
/// Carries the wire fields plus the lifecycle, idempotency, and lease metadata the durable
/// workflow and the scheduled-payment processor need. Projected to the public
/// <see cref="PaymentResponse"/> at the API boundary.
/// </summary>
public sealed record PaymentRecord
{
    public required string PaymentId { get; init; }

    public required string BillerId { get; init; }

    public required string InvoiceId { get; init; }

    public string? PayerAccountId { get; init; }

    public required string Method { get; init; }

    public required int AmountCents { get; init; }

    public required int FeeCents { get; init; }

    public required int TotalCents { get; init; }

    public required string Confirmation { get; init; }

    public DateOnly? ScheduledFor { get; init; }

    /// <summary>Groups the payments of one enrolled installment plan; null for a one-time payment.</summary>
    public string? InstallmentPlanId { get; init; }

    /// <summary>Zero-based position of this installment within its plan; null when not an installment.</summary>
    public int? InstallmentSequence { get; init; }

    /// <summary>Total number of installments in this payment's plan; null when not an installment.</summary>
    public int? InstallmentCount { get; init; }

    /// <summary>True when this payment belongs to an installment plan (a scheduled partial payment).</summary>
    [JsonIgnore]
    public bool IsInstallment => InstallmentPlanId is not null;

    public required string ReceiptMessage { get; init; }

    public required PaymentLifecycle Lifecycle { get; init; }

    /// <summary>
    /// Durable client idempotency key (from the <c>Idempotency-Key</c> header). When present, a
    /// retried request maps to the same <see cref="PaymentId"/> and replays the original result.
    /// </summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>
    /// Fingerprint of the request fields the idempotency key stands for. Reusing the same key with
    /// a materially different request is a client error and is rejected rather than silently
    /// replaying an unrelated payment.
    /// </summary>
    public string? RequestFingerprint { get; init; }

    /// <summary>Reason a payment reached <see cref="PaymentLifecycle.Failed"/>, for diagnostics.</summary>
    public string? FailureReason { get; init; }

    /// <summary>
    /// Exclusive lease held by a processor while it drives a pending/scheduled record forward.
    /// A record may be re-claimed once this passes, so a crashed processor never wedges it.
    /// </summary>
    public DateTimeOffset? LeaseUntil { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    [JsonIgnore]
    public string? ETag { get; init; }

    /// <summary>True once the payment has reached a terminal or scheduled (client-visible) state.</summary>
    public bool IsFinalized => Lifecycle is not PaymentLifecycle.Pending;

    public PaymentStatus WireStatus => Lifecycle switch
    {
        PaymentLifecycle.Succeeded => PaymentStatus.Succeeded,
        PaymentLifecycle.Scheduled => PaymentStatus.Scheduled,
        PaymentLifecycle.Failed => PaymentStatus.Failed,
        // A pending record is never surfaced on the wire (GET hides it until finalized);
        // mapped defensively to scheduled to avoid ever reporting an unconfirmed "succeeded".
        _ => PaymentStatus.Scheduled,
    };

    public PaymentResponse ToResponse() => new(
        PaymentId: PaymentId,
        BillerId: BillerId,
        InvoiceId: InvoiceId,
        PayerAccountId: PayerAccountId,
        Method: Method,
        AmountCents: AmountCents,
        FeeCents: FeeCents,
        TotalCents: TotalCents,
        Confirmation: Confirmation,
        Status: WireStatus,
        ScheduledFor: ScheduledFor,
        ReceiptMessage: ReceiptMessage,
        CreatedAt: CreatedAt,
        InstallmentPlanId: InstallmentPlanId,
        InstallmentSequence: InstallmentSequence,
        InstallmentCount: InstallmentCount);

    /// <summary>
    /// The payment id to use for a request. When a client supplies an idempotency key the id is
    /// derived deterministically from <paramref name="billerId"/> + key, so a retried request
    /// resolves to the same document (making the durable dedupe a single conditional insert);
    /// otherwise a fresh GUID is used.
    /// </summary>
    public static string DeriveId(string billerId, string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Guid.NewGuid().ToString();
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{billerId}\n{idempotencyKey}"));
        return new Guid(bytes.AsSpan(0, 16)).ToString();
    }

    /// <summary>
    /// Stable fingerprint of the request fields an idempotency key represents. A retry must carry
    /// the same values; a different one is rejected as a key-reuse conflict.
    /// </summary>
    public static string Fingerprint(
        string invoiceId,
        string method,
        string? payerAccountId,
        DateOnly? scheduledFor,
        int amountCents = 0,
        int? installmentSequence = null)
        => string.Join(
            '\u001f',
            invoiceId,
            method,
            payerAccountId ?? string.Empty,
            scheduledFor?.DayNumber.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            amountCents.ToString(CultureInfo.InvariantCulture),
            installmentSequence?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
}

/// <summary>Result of <see cref="Storage.IPaymentStore.BeginAsync"/>.</summary>
/// <param name="Created">True when this call inserted the record; false when it already existed.</param>
/// <param name="Record">The authoritative stored record (existing one on a dedupe hit).</param>
public sealed record PaymentBeginResult(bool Created, PaymentRecord Record);
