using IC.Payment.Contracts.V1.Payments;

namespace IC.Payment.Api.Storage;

public interface IPaymentStore
{
    void Add(PaymentResponse payment);

    PaymentResponse? Find(string billerId, string paymentId);
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
}
