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

/// <summary>Wire tokens pinned at the type level so serialization is host-independent.</summary>
[JsonConverter(typeof(PaymentStatusJsonConverter))]
public enum PaymentStatus
{
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
