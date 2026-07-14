using Pronto.Invoice.Api.Domain;
using Pronto.Invoice.Api.Seeding;
using Xunit;

namespace Pronto.Invoice.Api.Tests;

public sealed class FakeInvoiceFactoryTests
{
    private static readonly DateOnly Today = new(2026, 7, 14);

    [Fact]
    public void CreateDefaultsToFourInvoicesWhenCountOmitted()
    {
        var invoices = FakeInvoiceFactory.Create("b_1", "ACCT-1", count: null, billType: null, Today);

        Assert.Equal(4, invoices.Count);
    }

    [Theory]
    [InlineData(0, 1)]     // below the floor clamps up to 1
    [InlineData(3, 3)]     // in range passes through
    [InlineData(100, 24)]  // above the ceiling clamps down to MaxCount
    public void CreateClampsCountToSupportedRange(int requested, int expected)
    {
        var invoices = FakeInvoiceFactory.Create("b_1", "ACCT-1", requested, billType: null, Today);

        Assert.Equal(expected, invoices.Count);
    }

    [Fact]
    public void CreateThemesDescriptionsByBillType()
    {
        var invoices = FakeInvoiceFactory.Create("b_1", "ACCT-1", count: 4, billType: "Utility", Today);

        Assert.Contains(invoices, i => i.Description == "Water & sewer service");
        Assert.Contains(invoices, i => i.Description == "Electricity usage");
    }

    [Fact]
    public void CreateUsesGenericDescriptionsForUnknownBillType()
    {
        var invoices = FakeInvoiceFactory.Create("b_1", "ACCT-1", count: 1, billType: "Spaceships", Today);

        Assert.Equal("Monthly statement", invoices[0].Description);
    }

    [Fact]
    public void CreateStaggersDueDatesTwoWeeksOutThenWeekly()
    {
        var invoices = FakeInvoiceFactory.Create("b_1", "ACCT-1", count: 3, billType: null, Today);

        Assert.Equal(Today.AddDays(14), invoices[0].DueDate);
        Assert.Equal(Today.AddDays(21), invoices[1].DueDate);
        Assert.Equal(Today.AddDays(28), invoices[2].DueDate);
    }

    [Fact]
    public void CreateStampsBillerAccountAndDueStatusOnEveryInvoice()
    {
        var invoices = FakeInvoiceFactory.Create("b_42", "ACCT-9", count: 5, billType: null, Today);

        Assert.All(invoices, i =>
        {
            Assert.Equal("b_42", i.BillerId);
            Assert.Equal("ACCT-9", i.AccountNumber);
            Assert.Equal(InvoiceStatus.Due, i.Status);
            Assert.False(string.IsNullOrWhiteSpace(i.Id));
            Assert.True(i.AmountCents > 0);
        });
    }
}
