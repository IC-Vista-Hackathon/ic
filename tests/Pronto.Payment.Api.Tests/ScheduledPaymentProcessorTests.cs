using Pronto.Invoice.Contracts.V1.Invoices;
using Pronto.Payment.Api;
using Pronto.Payment.Api.Domain;
using Pronto.Payment.Api.Storage;
using Pronto.Payment.Api.Workflow;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Pronto.Payment.Api.Tests;

public sealed class ScheduledPaymentProcessorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryPaymentStore store = new();
    private readonly FakeInvoiceClient invoices = new();
    private readonly MutableTimeProvider clock = new(Now);
    private readonly PaymentWorkflow workflow;
    private readonly ScheduledPaymentProcessor processor;

    public ScheduledPaymentProcessorTests()
    {
        workflow = new PaymentWorkflow(store, invoices, clock, NullLogger<PaymentWorkflow>.Instance);
        processor = new ScheduledPaymentProcessor(
            store,
            workflow,
            clock,
            Options.Create(new PaymentProcessingOptions { LeaseSeconds = 60, PendingRecoveryGraceSeconds = 120 }),
            NullLogger<ScheduledPaymentProcessor>.Instance);
    }

    private static PaymentRecord Pending(string biller, string invoiceId, string paymentId, DateOnly? scheduledFor, DateTimeOffset updatedAt)
        => new()
        {
            PaymentId = paymentId,
            BillerId = biller,
            InvoiceId = invoiceId,
            Method = "card",
            AmountCents = 1000,
            FeeCents = 25,
            TotalCents = 1025,
            Confirmation = "PRONTO-ABC123",
            ScheduledFor = scheduledFor,
            ReceiptMessage = "Thanks!",
            Lifecycle = PaymentLifecycle.Pending,
            CreatedAt = updatedAt,
            UpdatedAt = updatedAt,
        };

    private async Task<string> SeedScheduledAsync(string biller, DateOnly scheduledFor)
    {
        var invoice = invoices.AddDueInvoice(biller, 1000);
        var paymentId = Guid.NewGuid().ToString();
        await store.BeginAsync(Pending(biller, invoice.Id, paymentId, scheduledFor, Now));
        await workflow.DriveInitialAsync((await store.FindAsync(biller, paymentId))!, default); // -> scheduled + invoice scheduled
        return invoice.Id;
    }

    [Fact]
    public async Task ProcessSettlesDueScheduledPayment()
    {
        var biller = Guid.NewGuid().ToString();
        var invoiceId = await SeedScheduledAsync(biller, new DateOnly(2026, 7, 15));

        var worked = await processor.ProcessOnceAsync(default);

        Assert.True(worked);
        Assert.Equal(InvoiceStatus.Paid, invoices.StatusOf(biller, invoiceId));
    }

    [Fact]
    public async Task ProcessReturnsFalseWhenNothingDue()
    {
        var biller = Guid.NewGuid().ToString();
        await SeedScheduledAsync(biller, new DateOnly(2026, 8, 1)); // future

        Assert.False(await processor.ProcessOnceAsync(default));
    }

    [Fact]
    public async Task ProcessRecoversStrandedPending()
    {
        var biller = Guid.NewGuid().ToString();
        var invoice = invoices.AddDueInvoice(biller, 1000);
        var paymentId = Guid.NewGuid().ToString();
        // Pending written but the invoice transition never happened (crash), long ago.
        await store.BeginAsync(Pending(biller, invoice.Id, paymentId, null, Now));
        clock.Advance(TimeSpan.FromSeconds(300));

        var worked = await processor.ProcessOnceAsync(default);

        Assert.True(worked);
        Assert.Equal(PaymentLifecycle.Succeeded, (await store.FindAsync(biller, paymentId))!.Lifecycle);
        Assert.Equal(InvoiceStatus.Paid, invoices.StatusOf(biller, invoice.Id));
    }

    [Fact]
    public async Task TransientFailureReleasesLeaseAndRetrySucceeds()
    {
        var biller = Guid.NewGuid().ToString();
        var invoiceId = await SeedScheduledAsync(biller, new DateOnly(2026, 7, 15));

        var throwOnce = 1;
        invoices.OnUpdateStatus = (_, _, _) =>
        {
            if (Interlocked.Exchange(ref throwOnce, 0) == 1)
            {
                throw new InvalidOperationException("transient");
            }
        };

        Assert.True(await processor.ProcessOnceAsync(default)); // claims, transient failure, releases lease
        Assert.Equal(InvoiceStatus.Scheduled, invoices.StatusOf(biller, invoiceId)); // not settled yet

        Assert.True(await processor.ProcessOnceAsync(default)); // retry succeeds
        Assert.Equal(InvoiceStatus.Paid, invoices.StatusOf(biller, invoiceId));
    }

    [Fact]
    public async Task ConcurrentProcessorsSettleEachPaymentExactlyOnce()
    {
        var biller = Guid.NewGuid().ToString();
        var invoiceIds = new List<string>();
        for (var i = 0; i < 15; i++)
        {
            invoiceIds.Add(await SeedScheduledAsync(biller, new DateOnly(2026, 7, 15)));
        }

        // Many competing processor passes; leases must prevent double-settlement.
        await Task.WhenAll(Enumerable.Range(0, 40)
            .Select(_ => Task.Run(async () =>
            {
                while (await processor.ProcessOnceAsync(default))
                {
                }
            })));

        Assert.All(invoiceIds, id => Assert.Equal(InvoiceStatus.Paid, invoices.StatusOf(biller, id)));
        Assert.Equal(15 * 2, invoices.AppliedTransitions); // 15 schedule + 15 settle, none duplicated
    }
}
