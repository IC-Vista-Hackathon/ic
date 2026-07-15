using Pronto.Payment.Api.Domain;
using Pronto.Payment.Api.Storage;
using Xunit;

namespace Pronto.Payment.Api.Tests;

public sealed class InMemoryPaymentStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    private static PaymentRecord Record(
        string billerId,
        string paymentId,
        PaymentLifecycle lifecycle = PaymentLifecycle.Pending,
        DateOnly? scheduledFor = null,
        string? payerAccountId = null,
        string? invoiceId = null,
        DateTimeOffset? updatedAt = null,
        DateTimeOffset? leaseUntil = null) => new()
    {
        PaymentId = paymentId,
        BillerId = billerId,
        InvoiceId = invoiceId ?? "inv-1",
        PayerAccountId = payerAccountId,
        Method = "card",
        AmountCents = 1000,
        FeeCents = 25,
        TotalCents = 1025,
        Confirmation = "PRONTO-ABC123",
        ScheduledFor = scheduledFor,
        ReceiptMessage = "Thanks!",
        Lifecycle = lifecycle,
        LeaseUntil = leaseUntil,
        CreatedAt = updatedAt ?? Now,
        UpdatedAt = updatedAt ?? Now,
    };

    [Fact]
    public async Task BeginIsIdempotentBySecondCallReturningExisting()
    {
        var store = new InMemoryPaymentStore();
        var first = await store.BeginAsync(Record("b", "p1", invoiceId: "inv-1"));
        var second = await store.BeginAsync(Record("b", "p1", invoiceId: "inv-DIFFERENT"));

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal("inv-1", second.Record.InvoiceId); // original wins; no overwrite
    }

    [Fact]
    public async Task ConcurrentBeginCreatesExactlyOnce()
    {
        var store = new InMemoryPaymentStore();
        var results = await Task.WhenAll(Enumerable.Range(0, 64)
            .Select(_ => Task.Run(() => store.BeginAsync(Record("b", "p1")))));

        Assert.Equal(1, results.Count(r => r.Created));
    }

    [Fact]
    public async Task ListExcludesPendingAndFilters()
    {
        var store = new InMemoryPaymentStore();
        await store.SaveAsync(Record("b", "p-pending", PaymentLifecycle.Pending, payerAccountId: "payer-1"));
        await store.SaveAsync(Record("b", "p-ok", PaymentLifecycle.Succeeded, payerAccountId: "payer-1", invoiceId: "inv-1"));
        await store.SaveAsync(Record("b", "p-other", PaymentLifecycle.Succeeded, payerAccountId: "payer-2", invoiceId: "inv-2"));

        var all = await store.ListAsync("b", null, null, default);
        Assert.Equal(2, all.Count); // pending excluded

        var filtered = await store.ListAsync("b", "payer-1", null, default);
        Assert.Equal("p-ok", Assert.Single(filtered).PaymentId);
    }

    [Fact]
    public async Task ClaimLeasesDueScheduledPaymentAndBlocksSecondClaim()
    {
        var store = new InMemoryPaymentStore();
        await store.SaveAsync(Record("b", "p1", PaymentLifecycle.Scheduled, scheduledFor: new DateOnly(2026, 7, 15)));
        var asOf = new DateOnly(2026, 7, 15);
        var lease = Now.AddSeconds(60);

        var claimed = await store.ClaimDueAsync(asOf, Now, Now.AddSeconds(-120), lease);
        var second = await store.ClaimDueAsync(asOf, Now, Now.AddSeconds(-120), lease);

        Assert.NotNull(claimed);
        Assert.Equal("p1", claimed!.PaymentId);
        Assert.Null(second); // still leased → not re-claimable
    }

    [Fact]
    public async Task ClaimReacquiresAfterLeaseExpiry()
    {
        var store = new InMemoryPaymentStore();
        await store.SaveAsync(Record("b", "p1", PaymentLifecycle.Scheduled, scheduledFor: new DateOnly(2026, 7, 15)));
        var asOf = new DateOnly(2026, 7, 15);

        await store.ClaimDueAsync(asOf, Now, Now.AddSeconds(-120), Now.AddSeconds(60));
        var later = Now.AddSeconds(120);
        var reclaimed = await store.ClaimDueAsync(asOf, later, later.AddSeconds(-120), later.AddSeconds(60));

        Assert.NotNull(reclaimed);
    }

    [Fact]
    public async Task ClaimIgnoresFutureScheduledAndFreshPending()
    {
        var store = new InMemoryPaymentStore();
        await store.SaveAsync(Record("b", "future", PaymentLifecycle.Scheduled, scheduledFor: new DateOnly(2026, 8, 1)));
        await store.SaveAsync(Record("b", "fresh", PaymentLifecycle.Pending, updatedAt: Now));

        var claimed = await store.ClaimDueAsync(new DateOnly(2026, 7, 15), Now, Now.AddSeconds(-120), Now.AddSeconds(60));

        Assert.Null(claimed);
    }

    [Fact]
    public async Task ClaimRecoversStalePending()
    {
        var store = new InMemoryPaymentStore();
        var stale = Now.AddSeconds(-300);
        await store.SaveAsync(Record("b", "stranded", PaymentLifecycle.Pending, updatedAt: stale));

        var claimed = await store.ClaimDueAsync(new DateOnly(2026, 7, 15), Now, Now.AddSeconds(-120), Now.AddSeconds(60));

        Assert.NotNull(claimed);
        Assert.Equal("stranded", claimed!.PaymentId);
    }

    [Fact]
    public async Task ConcurrentClaimGivesEachDueRecordToOneCaller()
    {
        var store = new InMemoryPaymentStore();
        for (var i = 0; i < 20; i++)
        {
            await store.SaveAsync(Record("b", $"p{i}", PaymentLifecycle.Scheduled, scheduledFor: new DateOnly(2026, 7, 15)));
        }

        var asOf = new DateOnly(2026, 7, 15);
        var claims = await Task.WhenAll(Enumerable.Range(0, 40)
            .Select(_ => Task.Run(() => store.ClaimDueAsync(asOf, Now, Now.AddSeconds(-120), Now.AddSeconds(60)))));

        var claimedIds = claims.Where(c => c is not null).Select(c => c!.PaymentId).ToList();
        Assert.Equal(20, claimedIds.Count);
        Assert.Equal(20, claimedIds.Distinct().Count()); // no record claimed twice
    }
}
