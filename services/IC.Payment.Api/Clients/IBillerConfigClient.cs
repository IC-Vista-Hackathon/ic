namespace IC.Payment.Api.Clients;

/// <summary>Fee/receipt slice of BillerConfiguration the Payment Service needs.</summary>
public sealed record BillerPaymentConfig(
    IReadOnlyList<string> PaymentMethods,
    decimal CardPercent,
    int AchFlatCents,
    bool PayerPaysFee,
    string ReceiptMessage);

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
        ReceiptMessage: "Thank you for your payment!");

    public Task<BillerPaymentConfig> GetAsync(string billerId, CancellationToken cancellationToken)
        => Task.FromResult(Default);
}
