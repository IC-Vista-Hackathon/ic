using Pronto.Invoice.Api.Domain;
using Pronto.Invoice.Api.Seeding;
using SeedInvoiceSpec = Pronto.Invoice.Contracts.V1.Invoices.SeedInvoiceSpec;
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
    public void CreateDoesNotHandAuthorAnHoaSetForOtherBillType()
    {
        // FR-6 regression guard: bill_type must never select a fixed hand-authored set. The removed
        // "other" branch used to return HOA dues / a pool special assessment / a Christmas-fine joke.
        var invoices = FakeInvoiceFactory.Create("b_1", "ACCT-1", count: null, billType: "other", Today);

        Assert.All(invoices, i =>
        {
            Assert.DoesNotContain("HOA", i.Description, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("special assessment", i.Description, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("All I Want for Christmas", i.Description, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void CreateMaterializesSuppliedSpecsVerbatim()
    {
        SeedInvoiceSpec[] specs =
        [
            new("Online apparel order", AmountCents: 6800, DueInDays: 14, PayerName: "Dana Wu",
                Type: "Order", StatusColor: "yellow", Note: "Due soon", NoteEmphasis: true),
            new("Store credit adjustment", AmountCents: 3000, DueInDays: 21, Type: "Store Credit"),
        ];

        var invoices = FakeInvoiceFactory.Create("b_1", "ACCT-1", count: 4, billType: "other", Today, specs);

        Assert.Equal(2, invoices.Count);
        Assert.Collection(invoices,
            first =>
            {
                Assert.Equal("Online apparel order", first.Description);
                Assert.Equal(6800, first.AmountCents);
                Assert.Equal(Today.AddDays(14), first.DueDate);
                Assert.Equal("Dana Wu", first.PayerName);
                Assert.Equal("Order", first.Type);
                Assert.Equal("yellow", first.StatusColor);
                Assert.Equal("Due soon", first.Note);
                Assert.True(first.NoteEmphasis);
                Assert.Equal(InvoiceStatus.Due, first.Status);
            },
            second =>
            {
                Assert.Equal("Store credit adjustment", second.Description);
                Assert.Equal(Today.AddDays(21), second.DueDate);
                Assert.Equal("Store Credit", second.Type);
                // A spec that omits a payer still gets one assigned deterministically.
                Assert.False(string.IsNullOrWhiteSpace(second.PayerName));
            });
    }

    [Fact]
    public void CreateSuppliedSpecsOverrideBillTypeAndCount()
    {
        SeedInvoiceSpec[] specs = [new("Facility rental permit", AmountCents: 7500, DueInDays: 14, Type: "Permit")];

        var invoices = FakeInvoiceFactory.Create("b_1", "ACCT-1", count: 10, billType: "utility", Today, specs);

        var invoice = Assert.Single(invoices);
        Assert.Equal("Facility rental permit", invoice.Description);
    }

    [Fact]
    public void CreateLeavesDemoHintsUnsetForGenericBillTypes()
    {
        var invoices = FakeInvoiceFactory.Create("b_1", "ACCT-1", count: 2, billType: "Utility", Today);

        Assert.All(invoices, i =>
        {
            Assert.Null(i.Type);
            Assert.Null(i.StatusColor);
            Assert.Null(i.Note);
        });
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
