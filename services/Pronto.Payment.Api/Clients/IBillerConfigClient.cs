namespace Pronto.Payment.Api.Clients;

/// <summary>
/// Fee/receipt + payment-policy slice of BillerConfiguration the Payment Service needs. The
/// installment/partial fields are the biller's <em>policy</em>: the server validates every
/// requested amount and plan against them, so a client can never pay outside what the biller
/// allows regardless of what the UI offers.
/// </summary>
public sealed record BillerPaymentConfig(
    IReadOnlyList<string> PaymentMethods,
    decimal CardPercent,
    int AchFlatCents,
    bool PayerPaysFee,
    string ReceiptMessage,
    bool PartialPaymentsAllowed = false,
    int MinPartialPaymentCents = 0,
    bool InstallmentsAllowed = false,
    int MaxInstallments = 0);

public interface IBillerConfigClient
{
    Task<BillerPaymentConfig> GetAsync(string billerId, CancellationToken cancellationToken);
}

/// <summary>
/// Demo defaults. BillerExperience contracts don't expose fees yet (flagged to that team);
/// swap this for an HTTP client when their config read endpoint lands.
/// </summary>
public sealed class DemoBillerConfigClient : IBillerConfigClient
{
    private static readonly BillerPaymentConfig Default = new(
        PaymentMethods: ["card", "ach", "applepay", "googlepay", "paypal"],
        CardPercent: 2.5m,
        AchFlatCents: 150,
        PayerPaysFee: true,
        ReceiptMessage: "Thank you for your payment!",
        PartialPaymentsAllowed: true,
        MinPartialPaymentCents: 500,
        InstallmentsAllowed: true,
        MaxInstallments: 12);

    public Task<BillerPaymentConfig> GetAsync(string billerId, CancellationToken cancellationToken)
        => Task.FromResult(Default);
}
