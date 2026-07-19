using System.Collections.Concurrent;
using Pronto.Payment.Api.Clients;

namespace Pronto.Payment.Api.Tests;

/// <summary>
/// Test double for the biller policy slice. Exposes the whole <see cref="BillerPaymentConfig"/> via
/// <see cref="Config"/> so partial-payment / installment policy can be varied per test (a biller that
/// forbids partials, one that caps installments), plus convenience setters mirroring
/// <see cref="DemoBillerConfigClient"/> and a per-biller <see cref="BillerSettlementState"/> override
/// so the settle-eligibility gate can be exercised deterministically.
/// </summary>
public sealed class FakeBillerConfigClient : IBillerConfigClient
{
    private readonly ConcurrentDictionary<string, BillerSettlementState> states = new(StringComparer.Ordinal);

    public BillerPaymentConfig Config { get; set; } = new(
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

    public IReadOnlyList<string> PaymentMethods
    {
        get => Config.PaymentMethods;
        set => Config = Config with { PaymentMethods = value };
    }

    public decimal CardPercent
    {
        get => Config.CardPercent;
        set => Config = Config with { CardPercent = value };
    }

    public int AchFlatCents
    {
        get => Config.AchFlatCents;
        set => Config = Config with { AchFlatCents = value };
    }

    public bool PayerPaysFee
    {
        get => Config.PayerPaysFee;
        set => Config = Config with { PayerPaysFee = value };
    }

    public string ReceiptMessage
    {
        get => Config.ReceiptMessage;
        set => Config = Config with { ReceiptMessage = value };
    }

    public void SetSettlementState(string billerId, BillerSettlementState state)
        => states[billerId] = state;

    public Task<BillerPaymentConfig> GetAsync(string billerId, CancellationToken cancellationToken)
    {
        var config = states.TryGetValue(billerId, out var state)
            ? Config with { SettlementState = state }
            : Config;
        return Task.FromResult(config);
    }
}
