using Pronto.Invoice.Contracts.V1.Invoices;
using Pronto.Payment.Api.Assurance;
using Pronto.Payment.Api.Clients;
using Pronto.Payment.Api.Storage;
using Pronto.Payment.Api.Workflow;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Pronto.Payment.Api.Tests;

public sealed class CanaryPaymentRunnerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryPaymentStore store = new();
    private readonly FakeInvoiceClient invoices = new();
    private readonly DemoBillerConfigClient configs = new();
    private readonly MutableTimeProvider clock = new(Now);
    private readonly CanaryPaymentRunner runner;

    public CanaryPaymentRunnerTests()
    {
        var workflow = new PaymentWorkflow(store, invoices, clock, NullLogger<PaymentWorkflow>.Instance);
        runner = new CanaryPaymentRunner(
            store, invoices, configs, workflow, clock, NullLogger<CanaryPaymentRunner>.Instance);
    }

    private CanaryTarget SeedTarget(string method = "card", int amountCents = 8420)
    {
        var biller = Guid.NewGuid().ToString();
        var invoice = invoices.AddDueInvoice(biller, amountCents);
        return new CanaryTarget(biller, invoice.Id, method, $"canary:{biller}");
    }

    [Fact]
    public async Task HappyPathSettlesAndIsFlaggedAsCanary()
    {
        var target = SeedTarget();

        var outcome = await runner.RunAsync(target, default);

        Assert.True(outcome.Settled);
        Assert.False(outcome.IdempotentReplay);
        Assert.StartsWith("PRONTO-", outcome.Confirmation!, StringComparison.Ordinal);
        Assert.Equal(8420, outcome.AmountCents);
        Assert.Equal(211, outcome.FeeCents); // 2.5% of 8420 away-from-zero
        Assert.Equal(8631, outcome.TotalCents);
        Assert.Equal(InvoiceStatus.Paid, invoices.StatusOf(target.BillerId, target.InvoiceId));

        var record = await store.FindAsync(target.BillerId, outcome.PaymentId!);
        Assert.True(record!.IsCanary);
    }

    [Fact]
    public async Task RetryIsExactlyOnceReplay()
    {
        var target = SeedTarget();

        var first = await runner.RunAsync(target, default);
        var second = await runner.RunAsync(target, default);

        Assert.True(first.Settled);
        Assert.True(second.Settled);
        Assert.True(second.IdempotentReplay);
        Assert.Equal(first.PaymentId, second.PaymentId);
        Assert.Equal(first.Confirmation, second.Confirmation);
        // Exactly one invoice transition despite two canary runs.
        Assert.Equal(1, invoices.AppliedTransitions);
    }

    [Fact]
    public async Task MethodNotEnabledFailsWithoutSettling()
    {
        var target = SeedTarget(method: "crypto");

        var outcome = await runner.RunAsync(target, default);

        Assert.False(outcome.Settled);
        Assert.Equal("method_not_enabled", outcome.FailureCode);
        Assert.Null(outcome.PaymentId);
    }

    [Fact]
    public async Task MissingCanaryInvoiceFails()
    {
        var biller = Guid.NewGuid().ToString();
        var target = new CanaryTarget(biller, "no-such-invoice", "card", $"canary:{biller}");

        var outcome = await runner.RunAsync(target, default);

        Assert.False(outcome.Settled);
        Assert.Equal("invoice_not_found", outcome.FailureCode);
    }

    [Fact]
    public async Task AlreadyPaidCanaryInvoiceIsNotPayable()
    {
        var target = SeedTarget();
        await runner.RunAsync(target, default); // settles the invoice

        // A fresh idempotency key against the now-paid invoice must not settle again.
        var replayTarget = target with { IdempotencyKey = $"{target.IdempotencyKey}:again" };
        var outcome = await runner.RunAsync(replayTarget, default);

        Assert.False(outcome.Settled);
        Assert.Equal("canary_invoice_not_payable", outcome.FailureCode);
    }

    [Fact]
    public async Task ReplayAfterFeeConfigChangeStillSettles()
    {
        var mutable = new MutableBillerConfigClient();
        var workflow = new PaymentWorkflow(store, invoices, clock, NullLogger<PaymentWorkflow>.Instance);
        var mutableRunner = new CanaryPaymentRunner(
            store, invoices, mutable, workflow, clock, NullLogger<CanaryPaymentRunner>.Instance);

        var biller = Guid.NewGuid().ToString();
        var invoice = invoices.AddDueInvoice(biller, 10000);
        var target = new CanaryTarget(biller, invoice.Id, "card", $"canary:{biller}");

        var first = await mutableRunner.RunAsync(target, default);
        Assert.True(first.Settled);
        Assert.Equal(250, first.FeeCents); // 2.5% of 10000

        // The biller changes its card fee after the original settlement; a replay must not flag the
        // still-consistent stored fee/total as a mismatch against the new config.
        mutable.CardPercent = 3.5m;
        var replay = await mutableRunner.RunAsync(target, default);

        Assert.True(replay.Settled);
        Assert.True(replay.IdempotentReplay);
        Assert.Null(replay.FailureCode);
        Assert.Equal(250, replay.FeeCents);
    }

    [Fact]
    public async Task RunAllReportsAggregateOk()
    {
        var good = SeedTarget();
        var bad = SeedTarget(method: "crypto");

        var result = await runner.RunAllAsync([good, bad], default);

        Assert.Equal(2, result.TargetCount);
        Assert.False(result.Ok);
        Assert.Single(result.Outcomes, o => o.Settled);
    }

    private sealed class MutableBillerConfigClient : IBillerConfigClient
    {
        public decimal CardPercent { get; set; } = 2.5m;

        public Task<BillerPaymentConfig> GetAsync(string billerId, CancellationToken cancellationToken)
            => Task.FromResult(new BillerPaymentConfig(
                PaymentMethods: ["card", "ach"],
                CardPercent: CardPercent,
                AchFlatCents: 150,
                PayerPaysFee: true,
                ReceiptMessage: "Thanks!"));
    }
}
