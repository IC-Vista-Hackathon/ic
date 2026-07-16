using System.Collections.Concurrent;
using Pronto.Payment.Api.Clients;

namespace Pronto.Payment.Api.Tests;

/// <summary>
/// In-process stand-in for the biller-config client. Mirrors <see cref="DemoBillerConfigClient"/>'s
/// fee/receipt defaults and treats every biller as published + compliance-approved unless a test
/// overrides its <see cref="BillerSettlementState"/>, so the settle-eligibility gate can be
/// exercised deterministically.
/// </summary>
public sealed class FakeBillerConfigClient : IBillerConfigClient
{
    private readonly ConcurrentDictionary<string, BillerSettlementState> states = new(StringComparer.Ordinal);

    public IReadOnlyList<string> PaymentMethods { get; set; } =
        ["card", "ach", "applepay", "googlepay", "paypal"];

    public decimal CardPercent { get; set; } = 2.5m;

    public int AchFlatCents { get; set; } = 150;

    public bool PayerPaysFee { get; set; } = true;

    public string ReceiptMessage { get; set; } = "Thank you for your payment!";

    public void SetSettlementState(string billerId, BillerSettlementState state)
        => states[billerId] = state;

    public Task<BillerPaymentConfig> GetAsync(string billerId, CancellationToken cancellationToken)
    {
        var state = states.TryGetValue(billerId, out var configured)
            ? configured
            : BillerSettlementState.Published;
        return Task.FromResult(new BillerPaymentConfig(
            PaymentMethods,
            CardPercent,
            AchFlatCents,
            PayerPaysFee,
            ReceiptMessage,
            state));
    }
}
