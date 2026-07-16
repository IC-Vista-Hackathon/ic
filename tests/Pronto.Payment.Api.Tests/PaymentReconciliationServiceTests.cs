using Pronto.Payment.Api.Assurance;
using Pronto.Payment.Api.Domain;
using Pronto.Payment.Api.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Pronto.Payment.Api.Tests;

public sealed class PaymentReconciliationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryPaymentStore store = new();
    private readonly MutableTimeProvider clock = new(Now);

    private PaymentReconciliationService NewService(AssuranceOptions? options = null) =>
        new(
            store,
            clock,
            Options.Create(options ?? new AssuranceOptions()),
            NullLogger<PaymentReconciliationService>.Instance);

    private static PaymentRecord Record(
        string biller,
        PaymentLifecycle lifecycle,
        string confirmation,
        int amount = 1000,
        int fee = 25,
        int? total = null,
        bool isCanary = false,
        DateTimeOffset? updatedAt = null) =>
        new()
        {
            PaymentId = Guid.NewGuid().ToString(),
            BillerId = biller,
            InvoiceId = Guid.NewGuid().ToString(),
            Method = "card",
            AmountCents = amount,
            FeeCents = fee,
            TotalCents = total ?? amount + fee,
            Confirmation = confirmation,
            ReceiptMessage = "Thanks!",
            Lifecycle = lifecycle,
            IsCanary = isCanary,
            CreatedAt = updatedAt ?? Now,
            UpdatedAt = updatedAt ?? Now,
        };

    [Fact]
    public async Task PassesOnConsistentData()
    {
        var biller = Guid.NewGuid().ToString();
        await store.BeginAsync(Record(biller, PaymentLifecycle.Succeeded, "PRONTO-AAA111"));
        await store.BeginAsync(Record(biller, PaymentLifecycle.Scheduled, "PRONTO-BBB222"));

        var result = await NewService().ReconcileAsync(null, null, default);

        Assert.True(result.Ok);
        Assert.Empty(result.Findings);
        Assert.Equal(2, result.SettledRecords);
    }

    [Fact]
    public async Task FlagsOrphanedPending()
    {
        var biller = Guid.NewGuid().ToString();
        await store.BeginAsync(Record(
            biller, PaymentLifecycle.Pending, "PRONTO-CCC333", updatedAt: Now - TimeSpan.FromSeconds(1000)));

        var result = await NewService().ReconcileAsync(null, null, default);

        Assert.False(result.Ok);
        Assert.Contains(result.Findings, f => f.Code == ReconciliationFindingCodes.OrphanedPending);
    }

    [Fact]
    public async Task RecentPendingIsNotOrphaned()
    {
        var biller = Guid.NewGuid().ToString();
        await store.BeginAsync(Record(
            biller, PaymentLifecycle.Pending, "PRONTO-DDD444", updatedAt: Now - TimeSpan.FromSeconds(10)));

        var result = await NewService().ReconcileAsync(null, null, default);

        Assert.True(result.Ok);
        Assert.Equal(1, result.PendingRecords);
    }

    [Fact]
    public async Task FlagsMissingRecordForClaimedConfirmation()
    {
        var biller = Guid.NewGuid().ToString();
        await store.BeginAsync(Record(biller, PaymentLifecycle.Succeeded, "PRONTO-REAL01"));

        var request = new ReconciliationRequest(["PRONTO-REAL01", "PRONTO-GHOST9"]);
        var result = await NewService().ReconcileAsync(null, request, default);

        Assert.False(result.Ok);
        var finding = Assert.Single(
            result.Findings, f => f.Code == ReconciliationFindingCodes.ConfirmationWithoutRecord);
        Assert.Equal("PRONTO-GHOST9", finding.Confirmation);
    }

    [Fact]
    public async Task ClaimedConfirmationWithRecordPasses()
    {
        var biller = Guid.NewGuid().ToString();
        await store.BeginAsync(Record(biller, PaymentLifecycle.Succeeded, "PRONTO-REAL02"));

        var result = await NewService().ReconcileAsync(
            null, new ReconciliationRequest(["PRONTO-REAL02"]), default);

        Assert.True(result.Ok);
    }

    [Fact]
    public async Task FlagsMismatchedTotal()
    {
        var biller = Guid.NewGuid().ToString();
        await store.BeginAsync(Record(
            biller, PaymentLifecycle.Succeeded, "PRONTO-EEE555", amount: 1000, fee: 25, total: 9999));

        var result = await NewService().ReconcileAsync(null, null, default);

        Assert.False(result.Ok);
        Assert.Contains(result.Findings, f => f.Code == ReconciliationFindingCodes.TotalMismatch);
    }

    [Fact]
    public async Task TotalEqualToAmountWhenBillerAbsorbsFeeIsConsistent()
    {
        var biller = Guid.NewGuid().ToString();
        await store.BeginAsync(Record(
            biller, PaymentLifecycle.Succeeded, "PRONTO-FFF666", amount: 1000, fee: 25, total: 1000));

        var result = await NewService().ReconcileAsync(null, null, default);

        Assert.True(result.Ok);
    }

    [Fact]
    public async Task FlagsDuplicateConfirmation()
    {
        var biller = Guid.NewGuid().ToString();
        await store.BeginAsync(Record(biller, PaymentLifecycle.Succeeded, "PRONTO-DUP777"));
        await store.BeginAsync(Record(biller, PaymentLifecycle.Succeeded, "PRONTO-DUP777"));

        var result = await NewService().ReconcileAsync(null, null, default);

        Assert.False(result.Ok);
        Assert.Contains(result.Findings, f => f.Code == ReconciliationFindingCodes.DuplicateConfirmation);
    }

    [Fact]
    public async Task FlagsSettledWithoutConfirmation()
    {
        var biller = Guid.NewGuid().ToString();
        await store.BeginAsync(Record(biller, PaymentLifecycle.Succeeded, confirmation: "  "));

        var result = await NewService().ReconcileAsync(null, null, default);

        Assert.False(result.Ok);
        Assert.Contains(result.Findings, f => f.Code == ReconciliationFindingCodes.SettledWithoutConfirmation);
    }

    [Fact]
    public async Task CanaryPaymentsAreExcludedFromGenuineReconciliation()
    {
        var biller = Guid.NewGuid().ToString();
        // A canary that would be an orphaned-pending divergence if counted as genuine traffic.
        await store.BeginAsync(Record(
            biller, PaymentLifecycle.Pending, "PRONTO-CAN888", isCanary: true,
            updatedAt: Now - TimeSpan.FromSeconds(1000)));

        var result = await NewService().ReconcileAsync(null, null, default);

        Assert.True(result.Ok);
        Assert.Equal(1, result.CanaryRecords);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task CanaryPaymentsCheckedWhenIncludeCanariesEnabled()
    {
        var biller = Guid.NewGuid().ToString();
        await store.BeginAsync(Record(
            biller, PaymentLifecycle.Pending, "PRONTO-CAN999", isCanary: true,
            updatedAt: Now - TimeSpan.FromSeconds(1000)));

        var result = await NewService(new AssuranceOptions { IncludeCanariesInReconciliation = true })
            .ReconcileAsync(null, null, default);

        Assert.False(result.Ok);
        Assert.Contains(result.Findings, f => f.Code == ReconciliationFindingCodes.OrphanedPending);
    }

    [Fact]
    public async Task ScopesToBiller()
    {
        var billerA = Guid.NewGuid().ToString();
        var billerB = Guid.NewGuid().ToString();
        await store.BeginAsync(Record(billerA, PaymentLifecycle.Succeeded, "PRONTO-AAA000"));
        await store.BeginAsync(Record(
            billerB, PaymentLifecycle.Succeeded, "PRONTO-BBB000", total: 5)); // divergent, other biller

        var result = await NewService().ReconcileAsync(billerA, null, default);

        Assert.True(result.Ok);
        Assert.Equal(1, result.TotalRecords);
    }
}
