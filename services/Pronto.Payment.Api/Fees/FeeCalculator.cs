using Pronto.ServiceDefaults.Errors;
using Pronto.Payment.Api.Clients;

namespace Pronto.Payment.Api.Fees;

public static class FeeCalculator
{
    /// <summary>
    /// card + wallets (applepay/googlepay/paypal) take the card percent; ach is flat.
    /// PayerPaysFee=false still reports the fee for display but does not add it to the total.
    /// </summary>
    /// <remarks>
    /// Money is integer cents and every arithmetic step is <c>checked</c>: a silently wrapped
    /// <see cref="int"/> would let an overflow turn a huge charge into a small (or negative) one,
    /// so any overflow is surfaced as a 400 rather than corrupting the amount.
    /// </remarks>
    public static (int FeeCents, int TotalCents) Calculate(
        BillerPaymentConfig config, string method, int amountCents)
    {
        if (amountCents < 0)
        {
            throw ServiceException.BadRequest(
                "invalid_amount", "invoice amount must be non-negative.");
        }

        if (config.AchFlatCents < 0 || config.CardPercent < 0)
        {
            throw ServiceException.BadRequest(
                "invalid_fee_config", "fee configuration must be non-negative.");
        }

        try
        {
            checked
            {
                var feeCents = method == "ach"
                    ? config.AchFlatCents
                    : (int)Math.Round(
                        amountCents * config.CardPercent / 100m, MidpointRounding.AwayFromZero);

                var totalCents = config.PayerPaysFee ? amountCents + feeCents : amountCents;
                return (feeCents, totalCents);
            }
        }
        catch (OverflowException)
        {
            throw ServiceException.BadRequest(
                "amount_overflow", "payment amount and fee exceed the supported range.");
        }
    }
}
