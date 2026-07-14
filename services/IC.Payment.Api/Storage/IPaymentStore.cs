using IC.Payment.Contracts.V1.Payments;

namespace IC.Payment.Api.Storage;

public interface IPaymentStore
{
    void Add(PaymentResponse payment);

    PaymentResponse? Find(string billerId, string paymentId);
    IReadOnlyList<PaymentResponse> List(string billerId, string? payerAccountId, string? invoiceId);
}

public sealed class InMemoryPaymentStore : IPaymentStore
{
    private readonly object gate = new();
    private readonly Dictionary<(string BillerId, string PaymentId), PaymentResponse> payments = [];

    public void Add(PaymentResponse payment)
    {
        lock (gate)
        {
            payments[(payment.BillerId, payment.PaymentId)] = payment;
        }
    }

    public PaymentResponse? Find(string billerId, string paymentId)
    {
        lock (gate)
        {
            return payments.GetValueOrDefault((billerId, paymentId));
        }
    }

    public IReadOnlyList<PaymentResponse> List(string billerId, string? payerAccountId, string? invoiceId)
    {
        lock (gate)
        {
            return payments.Values
                .Where(payment => payment.BillerId == billerId
                    && (payerAccountId is null || payment.PayerAccountId == payerAccountId)
                    && (invoiceId is null || payment.InvoiceId == invoiceId))
                .OrderByDescending(payment => payment.CreatedAt)
                .ToArray();
        }
    }
}
