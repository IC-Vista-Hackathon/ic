using Pronto.Payment.Contracts.V1.Payments;

namespace Pronto.Payment.Api.Storage;

public interface IPaymentStore
{
    Task AddAsync(PaymentResponse payment, CancellationToken cancellationToken = default);

    Task<PaymentResponse?> FindAsync(string billerId, string paymentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PaymentResponse>> ListAsync(
        string billerId, string? payerAccountId, string? invoiceId, CancellationToken cancellationToken = default);
}

public sealed class InMemoryPaymentStore : IPaymentStore
{
    private readonly object gate = new();
    private readonly Dictionary<(string BillerId, string PaymentId), PaymentResponse> payments = [];

    public Task AddAsync(PaymentResponse payment, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            payments[(payment.BillerId, payment.PaymentId)] = payment;
        }

        return Task.CompletedTask;
    }

    public Task<PaymentResponse?> FindAsync(
        string billerId, string paymentId, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            return Task.FromResult(payments.GetValueOrDefault((billerId, paymentId)));
        }
    }

    public Task<IReadOnlyList<PaymentResponse>> ListAsync(
        string billerId, string? payerAccountId, string? invoiceId, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            IReadOnlyList<PaymentResponse> results = payments.Values
                .Where(payment => payment.BillerId == billerId
                    && (payerAccountId is null || payment.PayerAccountId == payerAccountId)
                    && (invoiceId is null || payment.InvoiceId == invoiceId))
                .OrderByDescending(payment => payment.CreatedAt)
                .ToArray();
            return Task.FromResult(results);
        }
    }
}
