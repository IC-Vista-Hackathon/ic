namespace IC.Payment.Contracts.V1.Payments;

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
    string Confirmation,
    PaymentStatus Status,
    DateOnly? ScheduledFor,
    string ReceiptMessage,
    DateTimeOffset CreatedAt);

public enum PaymentStatus
{
    Scheduled,
    Succeeded,
    Failed
}
