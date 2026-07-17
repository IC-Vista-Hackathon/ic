using Pronto.Invoice.Contracts.V1.Invoices;
using Pronto.Payment.Api;
using Pronto.Payment.Api.Domain;
using Pronto.Payment.Api.Storage;
using Pronto.Payment.Api.Workflow;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Pronto.Payment.Api.Tests;

/// <summary>
/// Balance-aware settlement for partial payments and installment plans: a partial that leaves a
/// balance never flips the invoice; only the payment that clears it does. The invoice amount the
/// server looks up is the authority.
/// </summary>
public sealed class PartialInstallmentWorkflowTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryPaymentStore store = new();
    private readonly FakeInvoiceClient invoices = new();
    private readonly MutableTimeProvider clock = new(Now);
    private readonly PaymentWorkflow workflow;
    private readonly ScheduledPaymentProcessor processor;

    public PartialInstallmentWorkflowTests()
    {
        workflow = new PaymentWorkflow(store, invoices, clock, NullLogger<PaymentWorkflow>.Instance);
        processor = new ScheduledPaymentProcessor(
            store,
            workflow,
            clock,
            Options.Create(new PaymentProcessingOptions { LeaseSeconds = 60, PendingRecoveryGraceSeconds = 120 }),
            NullLogger<ScheduledPaymentProcessor>.Instance);
    }

    private async Task<PaymentRecord> DriveImmediatePartialAsync(string biller, string invoiceId, string paymentId, int amountCents)
    {
        var pending = new PaymentRecord
        {
            PaymentId = paymentId,
            BillerId = biller,
            InvoiceId = invoiceId,
            Method = "card",
            AmountCents = amountCents,
            FeeCents = 0,
            TotalCents = amountCents,
            Confirmation = "PRONTO-ABC123",
            ReceiptMessage = "Thanks!",
            Lifecycle = PaymentLifecycle.Pending,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        await store.BeginAsync(pending);
        return await workflow.DriveInitialAsync(pending, default);
    }

    [Fact]
    public async Task ImmediatePartialLeavesInvoiceDueUntilCleared()
    {
        var biller = Guid.NewGuid().ToString();
        var invoice = invoices.AddDueInvoice(biller, 1000);

        var first = await DriveImmediatePartialAsync(biller, invoice.Id, "p1", 400);
        Assert.Equal(PaymentLifecycle.Succeeded, first.Lifecycle);
        Assert.Equal(InvoiceStatus.Due, invoices.StatusOf(biller, invoice.Id));

        var second = await DriveImmediatePartialAsync(biller, invoice.Id, "p2", 600);
        Assert.Equal(PaymentLifecycle.Succeeded, second.Lifecycle);
        Assert.Equal(InvoiceStatus.Paid, invoices.StatusOf(biller, invoice.Id));
        Assert.Equal("p2", invoices.LastPaymentIdOf(biller, invoice.Id));
    }

    private async Task SeedInstallmentAsync(string biller, string invoiceId, string planId, int sequence, int amountCents, DateOnly due)
    {
        var record = new PaymentRecord
        {
            PaymentId = $"{planId}-{sequence}",
            BillerId = biller,
            InvoiceId = invoiceId,
            Method = "card",
            AmountCents = amountCents,
            FeeCents = 0,
            TotalCents = amountCents,
            Confirmation = "PRONTO-ABC123",
            ScheduledFor = due,
            InstallmentPlanId = planId,
            InstallmentSequence = sequence,
            InstallmentCount = 3,
            ReceiptMessage = "Thanks!",
            Lifecycle = PaymentLifecycle.Scheduled,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        await store.BeginAsync(record);
    }

    [Fact]
    public async Task InstallmentScheduleSettlesInvoiceOnlyOnFinalInstallment()
    {
        var biller = Guid.NewGuid().ToString();
        var invoice = invoices.AddDueInvoice(biller, 9000);
        var planId = "plan-1";
        await SeedInstallmentAsync(biller, invoice.Id, planId, 0, 3000, new DateOnly(2026, 7, 15));
        await SeedInstallmentAsync(biller, invoice.Id, planId, 1, 3000, new DateOnly(2026, 8, 15));
        await SeedInstallmentAsync(biller, invoice.Id, planId, 2, 3000, new DateOnly(2026, 9, 15));

        // First installment is due today: it settles but leaves a balance, so the invoice stays due.
        Assert.True(await processor.ProcessOnceAsync(default));
        Assert.Equal(InvoiceStatus.Due, invoices.StatusOf(biller, invoice.Id));
        Assert.False(await processor.ProcessOnceAsync(default)); // nothing else due yet

        clock.Advance(TimeSpan.FromDays(31));
        Assert.True(await processor.ProcessOnceAsync(default));
        Assert.Equal(InvoiceStatus.Due, invoices.StatusOf(biller, invoice.Id));

        clock.Advance(TimeSpan.FromDays(31));
        Assert.True(await processor.ProcessOnceAsync(default));
        Assert.Equal(InvoiceStatus.Paid, invoices.StatusOf(biller, invoice.Id));

        var settled = await store.ListAsync(biller, null, invoice.Id);
        Assert.All(settled, record => Assert.Equal(PaymentLifecycle.Succeeded, record.Lifecycle));
    }
}
