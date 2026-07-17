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
    BillerSettlementState SettlementState,
    bool PartialPaymentsAllowed = false,
    int MinPartialPaymentCents = 0,
    bool InstallmentsAllowed = false,
    int MaxInstallments = 0);

/// <summary>
/// Whether a biller's configuration has cleared the publish + compliance gate and is therefore
/// allowed to settle real payments. This is server-owned and derived from the biller's published
/// configuration and compliance review — it is NEVER agent- or client-writable, so no request field
/// can move a biller into a settle-eligible state.
/// </summary>
public enum BillerSettlementState
{
    /// <summary>Configuration has not been published; no payments may settle.</summary>
    Unpublished,

    /// <summary>Configuration is published but has not passed the compliance gate.</summary>
    ComplianceNotPassed,

    /// <summary>Published and compliance-approved — the only settle-eligible state.</summary>
    Published,
}

public interface IBillerConfigClient
{
    Task<BillerPaymentConfig> GetAsync(string billerId, CancellationToken cancellationToken);
}

/// <summary>
/// Demo defaults. BillerExperience contracts don't expose fees yet (flagged to that team);
/// swap this for an HTTP client when their config read endpoint lands. The demo biller is treated
/// as published + compliance-approved so local/preview flows can settle against fake rails.
/// </summary>
public sealed class DemoBillerConfigClient : IBillerConfigClient
{
    private static readonly BillerPaymentConfig Default = new(
        PaymentMethods: ["card", "ach", "applepay", "googlepay", "paypal"],
        CardPercent: 2.5m,
        AchFlatCents: 150,
        PayerPaysFee: true,
        ReceiptMessage: "Thank you for your payment!",
        SettlementState: BillerSettlementState.Published,
        PartialPaymentsAllowed: true,
        MinPartialPaymentCents: 500,
        InstallmentsAllowed: true,
        MaxInstallments: 12);

    public Task<BillerPaymentConfig> GetAsync(string billerId, CancellationToken cancellationToken)
        => Task.FromResult(Default);
}
