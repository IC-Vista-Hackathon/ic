using IC.Payment.Api.Clients;
using IC.Payment.Api.Fees;
using Xunit;

namespace IC.Payment.Api.Tests;

public sealed class FeeCalculatorTests
{
    private static BillerPaymentConfig Config(bool payerPaysFee) => new(
        PaymentMethods: ["card", "ach", "applepay"],
        CardPercent: 2.5m,
        AchFlatCents: 150,
        PayerPaysFee: payerPaysFee,
        ReceiptMessage: "Thanks!");

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
}
