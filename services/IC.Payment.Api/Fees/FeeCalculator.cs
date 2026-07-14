using IC.Payment.Api.Clients;

namespace IC.Payment.Api.Fees;

public static class FeeCalculator
{
    /// <summary>
    /// card + wallets (applepay/googlepay/paypal) take the card percent; ach is flat.
    /// PayerPaysFee=false still reports the fee for display but does not add it to the total.
    /// </summary>
    public static (int FeeCents, int TotalCents) Calculate(
        BillerPaymentConfig config, string method, int amountCents)
    {
        var feeCents = method == "ach"
            ? config.AchFlatCents
            : (int)Math.Round(
                amountCents * config.CardPercent / 100m, MidpointRounding.AwayFromZero);

        var totalCents = config.PayerPaysFee ? amountCents + feeCents : amountCents;
        return (feeCents, totalCents);
    }
}
