using Pronto.Payment.Api.Domain;
using Xunit;

namespace Pronto.Payment.Api.Tests;

public sealed class PaymentAmountsTests
{
    [Theory]
    [InlineData(10000, 3, new[] { 3334, 3333, 3333 })]
    [InlineData(9000, 3, new[] { 3000, 3000, 3000 })]
    [InlineData(1001, 4, new[] { 251, 250, 250, 250 })]
    [InlineData(500, 2, new[] { 250, 250 })]
    public void SplitDistributesRemainderAndSumsExactly(int outstanding, int count, int[] expected)
    {
        var amounts = PaymentAmounts.SplitIntoInstallments(outstanding, count);

        Assert.Equal(expected, amounts);
        Assert.Equal(outstanding, amounts.Sum());
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(-100, 3)]
    [InlineData(100, 0)]
    [InlineData(2, 3)] // balance smaller than the number of installments
    public void SplitRejectsInvalidInputs(int outstanding, int count)
        => Assert.Throws<ArgumentOutOfRangeException>(() => PaymentAmounts.SplitIntoInstallments(outstanding, count));
}
