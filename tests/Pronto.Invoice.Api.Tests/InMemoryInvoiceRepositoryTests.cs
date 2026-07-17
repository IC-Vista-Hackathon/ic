using Pronto.Invoice.Api.Domain;
using Pronto.Invoice.Api.Repositories;
using Xunit;

namespace Pronto.Invoice.Api.Tests;

public sealed class InMemoryInvoiceRepositoryTests
{
    private static InvoiceDocument Make(
        string billerId,
        string accountNumber,
        InvoiceStatus status = InvoiceStatus.Due) => MakeWithId(
            Guid.NewGuid().ToString(), billerId, accountNumber, status);

    private static InvoiceDocument MakeWithId(
        string id,
        string billerId,
        string accountNumber,
        InvoiceStatus status = InvoiceStatus.Due) => new()
    {
        Id = id,
        BillerId = billerId,
        AccountNumber = accountNumber,
        PayerName = "Test Payer",
        Description = "Test",
        AmountCents = 1000,
        DueDate = new DateOnly(2026, 8, 1),
        Status = status,
    };

    [Fact]
    public async Task GetOpenReturnsInvoicesForMatchingBillerAndAccount()
    {
        var repo = new InMemoryInvoiceRepository();
        await repo.AddRangeAsync([Make("b_1", "ACCT-1"), Make("b_1", "ACCT-1")]);

        var open = await repo.GetOpenAsync("b_1", "ACCT-1");

        Assert.Equal(2, open.Count);
    }

    [Fact]
    public async Task GetOpenExcludesOtherAccountNumbers()
    {
        var repo = new InMemoryInvoiceRepository();
        await repo.AddRangeAsync([Make("b_1", "ACCT-1"), Make("b_1", "ACCT-2")]);

        var open = await repo.GetOpenAsync("b_1", "ACCT-1");

        Assert.Single(open);
        Assert.Equal("ACCT-1", open[0].AccountNumber);
    }

    [Fact]
    public async Task GetOpenExcludesPaidInvoices()
    {
        var repo = new InMemoryInvoiceRepository();
        await repo.AddRangeAsync(
        [
            Make("b_1", "ACCT-1", InvoiceStatus.Due),
            Make("b_1", "ACCT-1", InvoiceStatus.Paid),
            Make("b_1", "ACCT-1", InvoiceStatus.Scheduled),
        ]);

        var open = await repo.GetOpenAsync("b_1", "ACCT-1");

        Assert.Equal(2, open.Count);
        Assert.DoesNotContain(open, i => i.Status == InvoiceStatus.Paid);
    }

    [Fact]
    public async Task GetOpenIsPartitionedByBiller()
    {
        var repo = new InMemoryInvoiceRepository();
        // Same account number under two different billers must not leak across the partition.
        await repo.AddRangeAsync([Make("b_1", "ACCT-1"), Make("b_2", "ACCT-1")]);

        var open = await repo.GetOpenAsync("b_1", "ACCT-1");

        Assert.Single(open);
        Assert.Equal("b_1", open[0].BillerId);
    }

    [Fact]
    public async Task GetOpenReturnsEmptyForUnknownBiller()
    {
        var repo = new InMemoryInvoiceRepository();

        var open = await repo.GetOpenAsync("nobody", "ACCT-1");

        Assert.Empty(open);
    }

    [Fact]
    public async Task PurgeByBillerRemovesOnlyThatBillersInvoices()
    {
        var repo = new InMemoryInvoiceRepository();
        await repo.AddRangeAsync([Make("b_1", "ACCT-1"), Make("b_1", "ACCT-2"), Make("b_2", "ACCT-1")]);

        await repo.PurgeByBillerAsync("b_1");

        Assert.Empty(await repo.GetOpenAsync("b_1", "ACCT-1"));
        Assert.Empty(await repo.GetOpenAsync("b_1", "ACCT-2"));
        Assert.Single(await repo.GetOpenAsync("b_2", "ACCT-1"));
    }

    [Fact]
    public async Task ReplaceAccountDropsSlotsAShrunkSetNoLongerCovers()
    {
        var repo = new InMemoryInvoiceRepository();
        // A larger prior seed: four slot ids for the account.
        await repo.ReplaceAccountAsync("b_1", "ACCT-1",
        [
            MakeWithId("slot-0", "b_1", "ACCT-1"),
            MakeWithId("slot-1", "b_1", "ACCT-1"),
            MakeWithId("slot-2", "b_1", "ACCT-1"),
            MakeWithId("slot-3", "b_1", "ACCT-1"),
        ]);

        // Re-seed with a smaller set (two slots): the extra slots must not linger.
        await repo.ReplaceAccountAsync("b_1", "ACCT-1",
        [
            MakeWithId("slot-0", "b_1", "ACCT-1"),
            MakeWithId("slot-1", "b_1", "ACCT-1"),
        ]);

        var remaining = await repo.GetByAccountAsync("b_1", "ACCT-1");
        Assert.Equal(2, remaining.Count);
        Assert.Equal(["slot-0", "slot-1"], remaining.Select(i => i.Id).OrderBy(id => id).ToArray());
    }

    [Fact]
    public async Task ReplaceAccountLeavesOtherAccountsUntouched()
    {
        var repo = new InMemoryInvoiceRepository();
        await repo.AddRangeAsync([Make("b_1", "ACCT-2")]);

        await repo.ReplaceAccountAsync("b_1", "ACCT-1", [Make("b_1", "ACCT-1")]);

        Assert.Single(await repo.GetByAccountAsync("b_1", "ACCT-1"));
        Assert.Single(await repo.GetByAccountAsync("b_1", "ACCT-2"));
    }

    [Fact]
    public async Task PurgeByUnknownBillerIsNoop()
    {
        var repo = new InMemoryInvoiceRepository();
        await repo.AddRangeAsync([Make("b_1", "ACCT-1")]);

        await repo.PurgeByBillerAsync("nobody");

        Assert.Single(await repo.GetOpenAsync("b_1", "ACCT-1"));
    }
}
