using Pronto.Invoice.Contracts.V1.Invoices;
using Pronto.Payment.Api.Domain;
using Pronto.Payment.Api.Storage;
using Pronto.Payment.Api.Workflow;
using Pronto.ServiceDefaults.Errors;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Pronto.Payment.Api.Tests;

public sealed class PaymentWorkflowTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryPaymentStore store = new();
    private readonly FakeInvoiceClient invoices = new();
    private readonly MutableTimeProvider clock = new(Now);
    private readonly PaymentWorkflow workflow;

    public PaymentWorkflowTests()
        => workflow = new PaymentWorkflow(store, invoices, clock, NullLogger<PaymentWorkflow>.Instance);

    private async Task<PaymentRecord> BeginPendingAsync(
        string billerId, string invoiceId, string paymentId, DateOnly? scheduledFor = null)
    {
        var pending = new PaymentRecord
        {
            PaymentId = paymentId,
            BillerId = billerId,
            InvoiceId = invoiceId,
            Method = "card",
            AmountCents = 1000,
            FeeCents = 25,
            TotalCents = 1025,
            Confirmation = "PRONTO-ABC123",
            ScheduledFor = scheduledFor,
            ReceiptMessage = "Thanks!",
            Lifecycle = PaymentLifecycle.Pending,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        var begin = await store.BeginAsync(pending);
        return begin.Record;
    }

    [Fact]
    public async Task DriveInitialSettlesImmediatePaymentAndBindsInvoice()
    {
        var biller = Guid.NewGuid().ToString();
        var invoice = invoices.AddDueInvoice(biller, 1000);
        var pending = await BeginPendingAsync(biller, invoice.Id, "p1");

        var result = await workflow.DriveInitialAsync(pending, default);

        Assert.Equal(PaymentLifecycle.Succeeded, result.Lifecycle);
        Assert.Equal(InvoiceStatus.Paid, invoices.StatusOf(biller, invoice.Id));
        Assert.Equal("p1", invoices.LastPaymentIdOf(biller, invoice.Id));
        Assert.Equal(PaymentLifecycle.Succeeded, (await store.FindAsync(biller, "p1"))!.Lifecycle);
    }

    [Fact]
    public async Task DriveInitialSchedulesWhenScheduledForSet()
    {
        var biller = Guid.NewGuid().ToString();
        var invoice = invoices.AddDueInvoice(biller, 1000);
        var pending = await BeginPendingAsync(biller, invoice.Id, "p1", new DateOnly(2026, 8, 1));

        var result = await workflow.DriveInitialAsync(pending, default);

        Assert.Equal(PaymentLifecycle.Scheduled, result.Lifecycle);
        Assert.Equal(InvoiceStatus.Scheduled, invoices.StatusOf(biller, invoice.Id));
    }

    [Fact]
    public async Task DriveInitialMarksFailedAndRethrowsWhenInvoiceRefuses()
    {
        var biller = Guid.NewGuid().ToString();
        var invoice = invoices.AddDueInvoice(biller, 1000);
        // Someone else already paid the invoice.
        await invoices.UpdateStatusAsync(biller, invoice.Id, new UpdateInvoiceStatusRequest(InvoiceStatus.Paid, "other"), default);
        var pending = await BeginPendingAsync(biller, invoice.Id, "p1");

        var exception = await Assert.ThrowsAsync<ServiceException>(
            () => workflow.DriveInitialAsync(pending, default));

        Assert.Equal("already_paid", exception.Code);
        var stored = await store.FindAsync(biller, "p1");
        Assert.Equal(PaymentLifecycle.Failed, stored!.Lifecycle);
        Assert.Equal("already_paid", stored.FailureReason);
    }

    [Fact]
    public async Task DriveInitialIsIdempotentWhenInvoiceAlreadyFlippedBySamePayment()
    {
        // Simulates a crash after the invoice flip but before finalizing the payment.
        var biller = Guid.NewGuid().ToString();
        var invoice = invoices.AddDueInvoice(biller, 1000);
        await invoices.UpdateStatusAsync(biller, invoice.Id, new UpdateInvoiceStatusRequest(InvoiceStatus.Paid, "p1"), default);
        var appliedAfterFlip = invoices.AppliedTransitions;
        var pending = await BeginPendingAsync(biller, invoice.Id, "p1");

        var result = await workflow.DriveInitialAsync(pending, default);

        Assert.Equal(PaymentLifecycle.Succeeded, result.Lifecycle);
        Assert.Equal(appliedAfterFlip, invoices.AppliedTransitions); // replay applied no second transition
    }

    [Fact]
    public async Task DriveInitialNoOpsOnAlreadyFinalizedRecord()
    {
        var biller = Guid.NewGuid().ToString();
        var invoice = invoices.AddDueInvoice(biller, 1000);
        var pending = await BeginPendingAsync(biller, invoice.Id, "p1");
        var finalized = await workflow.DriveInitialAsync(pending, default);

        var again = await workflow.DriveInitialAsync(finalized, default);

        Assert.Equal(PaymentLifecycle.Succeeded, again.Lifecycle);
        Assert.Equal(1, invoices.AppliedTransitions);
    }

    [Fact]
    public async Task SettleScheduledPaysBoundInvoice()
    {
        var biller = Guid.NewGuid().ToString();
        var invoice = invoices.AddDueInvoice(biller, 1000);
        var pending = await BeginPendingAsync(biller, invoice.Id, "p1", new DateOnly(2026, 8, 1));
        var scheduled = await workflow.DriveInitialAsync(pending, default);

        var settled = await workflow.SettleScheduledAsync(scheduled, default);

        Assert.Equal(PaymentLifecycle.Succeeded, settled.Lifecycle);
        Assert.Equal(InvoiceStatus.Paid, invoices.StatusOf(biller, invoice.Id));
    }
}
