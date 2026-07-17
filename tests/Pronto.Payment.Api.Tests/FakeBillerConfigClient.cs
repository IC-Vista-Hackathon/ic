using Pronto.Payment.Api.Clients;

namespace Pronto.Payment.Api.Tests;

/// <summary>
/// Test double for the biller policy slice so partial-payment and installment policy can be varied
/// per test (a biller that forbids partials, one that forbids installments, one that caps them).
/// </summary>
public sealed class FakeBillerConfigClient : IBillerConfigClient
{
    public BillerPaymentConfig Config { get; set; } = new(
        PaymentMethods: ["card", "ach"],
        CardPercent: 2.5m,
        AchFlatCents: 150,
        PayerPaysFee: true,
        ReceiptMessage: "Thank you for your payment!",
        PartialPaymentsAllowed: true,
        MinPartialPaymentCents: 500,
        InstallmentsAllowed: true,
        MaxInstallments: 12);

    public Task<BillerPaymentConfig> GetAsync(string billerId, CancellationToken cancellationToken)
        => Task.FromResult(Config);
}
