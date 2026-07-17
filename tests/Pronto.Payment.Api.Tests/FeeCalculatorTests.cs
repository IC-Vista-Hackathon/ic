using Pronto.Payment.Api.Clients;
using Pronto.Payment.Api.Fees;
using Pronto.ServiceDefaults.Errors;
using Xunit;

namespace Pronto.Payment.Api.Tests;

public sealed class FeeCalculatorTests
{
    private static BillerPaymentConfig Config(
        bool payerPaysFee, decimal cardPercent = 2.5m, int achFlatCents = 150) => new(
        PaymentMethods: ["card", "ach", "applepay"],
        CardPercent: cardPercent,
        AchFlatCents: achFlatCents,
        PayerPaysFee: payerPaysFee,
        ReceiptMessage: "Thanks!",
        SettlementState: BillerSettlementState.Published);

    [Theory]
    [InlineData("card")]
    [InlineData("applepay")]
    public void WalletMethodsTakeCardPercent(string method)
    {
        var (fee, total) = FeeCalculator.Calculate(Config(payerPaysFee: true), method, 10000);

        Assert.Equal(250, fee);
        Assert.Equal(10250, total);
    }

    [Fact]
    public void BillerAbsorbedFeeIsReportedButNotCharged()
    {
        var (fee, total) = FeeCalculator.Calculate(Config(payerPaysFee: false), "card", 10000);

        Assert.Equal(250, fee);
        Assert.Equal(10000, total); // fee shown for display, not added to the charge
    }

    [Fact]
    public void HalfCentRoundsAwayFromZero()
    {
        var (fee, _) = FeeCalculator.Calculate(Config(payerPaysFee: true), "card", 8420);

        Assert.Equal(211, fee); // 2.5% of 8420 = 210.5
    }

    [Fact]
    public void TotalOverflowIsRejectedNotWrapped()
    {
        // amount + fee would overflow Int32; checked arithmetic must surface a 400, never wrap
        // to a small/negative charge.
        var exception = Assert.Throws<ServiceException>(
            () => FeeCalculator.Calculate(Config(payerPaysFee: true, achFlatCents: 150), "ach", int.MaxValue));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("amount_overflow", exception.Code);
    }

    [Fact]
    public void PercentFeeOverflowIsRejected()
    {
        // 200% of int.MaxValue rounds past Int32 range; the decimal→int cast must not wrap.
        var exception = Assert.Throws<ServiceException>(
            () => FeeCalculator.Calculate(Config(payerPaysFee: false, cardPercent: 200m), "card", int.MaxValue));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("amount_overflow", exception.Code);
    }

    [Fact]
    public void NegativeAmountIsRejected()
    {
        var exception = Assert.Throws<ServiceException>(
            () => FeeCalculator.Calculate(Config(payerPaysFee: true), "card", -1));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("invalid_amount", exception.Code);
    }

    [Fact]
    public void MaxNonOverflowingTotalIsComputed()
    {
        // Boundary: biller absorbs the fee, so the total is exactly the amount — no overflow even
        // at int.MaxValue, and the fee is still reported.
        var (fee, total) = FeeCalculator.Calculate(Config(payerPaysFee: false, cardPercent: 0m), "card", int.MaxValue);

        Assert.Equal(0, fee);
        Assert.Equal(int.MaxValue, total);
    }

    [Fact]
    public void AchFlatFeeWithinRangeSucceeds()
    {
        var (fee, total) = FeeCalculator.Calculate(Config(payerPaysFee: true, achFlatCents: 150), "ach", 10000);

        Assert.Equal(150, fee);
        Assert.Equal(10150, total);
    }
}
