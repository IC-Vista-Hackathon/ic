using Pronto.Invoice.Contracts.V1.Invoices;
using Pronto.Payment.Api.Scheduling;
using Pronto.Payment.Api.Storage;
using Pronto.Payment.Contracts.V1.Payments;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Pronto.Payment.Api.Tests;

public sealed class ScheduledPaymentExecutorTests
{
    private readonly InMemoryPaymentStore store = new();
    private readonly FakeInvoiceClient invoices = new();
    private readonly ScheduledPaymentExecutor executor;

    public ScheduledPaymentExecutorTests()
        => executor = new ScheduledPaymentExecutor(store, invoices, NullLogger<ScheduledPaymentExecutor>.Instance);

    [Fact]
    public async Task DuePaymentIsExecutedAndInvoiceMarkedPaid()
    {
        var billerId = Guid.NewGuid().ToString();
        var invoice = invoices.AddDueInvoice(billerId, amountCents: 5000);
        var scheduled = await ScheduleAsync(billerId, invoice.Id, new DateOnly(2026, 7, 20));

        var executed = await executor.ExecuteDueAsync(new DateOnly(2026, 7, 20), CancellationToken.None);

        Assert.Equal(1, executed);
        Assert.Equal(InvoiceStatus.Paid, invoices.StatusOf(billerId, invoice.Id));
        var stored = await store.FindAsync(billerId, scheduled.PaymentId, CancellationToken.None);
        Assert.Equal(PaymentStatus.Succeeded, stored!.Status);
    }

    [Fact]
    public async Task FuturePaymentIsNotExecuted()
    {
        var billerId = Guid.NewGuid().ToString();
        var invoice = invoices.AddDueInvoice(billerId, amountCents: 5000);
        var scheduled = await ScheduleAsync(billerId, invoice.Id, new DateOnly(2026, 7, 24));

        var executed = await executor.ExecuteDueAsync(new DateOnly(2026, 7, 20), CancellationToken.None);

        Assert.Equal(0, executed);
        Assert.Equal(InvoiceStatus.Scheduled, invoices.StatusOf(billerId, invoice.Id));
        var stored = await store.FindAsync(billerId, scheduled.PaymentId, CancellationToken.None);
        Assert.Equal(PaymentStatus.Scheduled, stored!.Status);
    }

    [Fact]
    public async Task ReRunIsIdempotentAfterInvoiceAlreadyPaid()
    {
        var billerId = Guid.NewGuid().ToString();
        var invoice = invoices.AddDueInvoice(billerId, amountCents: 5000);
        var scheduled = await ScheduleAsync(billerId, invoice.Id, new DateOnly(2026, 7, 20));

        await executor.ExecuteDueAsync(new DateOnly(2026, 7, 20), CancellationToken.None);
        // Simulate a crash after the invoice was paid but before the payment was finalized:
        // reset the payment to Scheduled and re-run. The invoice is already Paid.
        await store.UpdateAsync(scheduled with { Status = PaymentStatus.Scheduled }, CancellationToken.None);

        var executed = await executor.ExecuteDueAsync(new DateOnly(2026, 7, 20), CancellationToken.None);

        Assert.Equal(1, executed);
        var stored = await store.FindAsync(billerId, scheduled.PaymentId, CancellationToken.None);
        Assert.Equal(PaymentStatus.Succeeded, stored!.Status);
    }

    [Fact]
    public async Task FinalizedPaymentIsNotPickedUpAgain()
    {
        var billerId = Guid.NewGuid().ToString();
        var invoice = invoices.AddDueInvoice(billerId, amountCents: 5000);
        await ScheduleAsync(billerId, invoice.Id, new DateOnly(2026, 7, 20));

        await executor.ExecuteDueAsync(new DateOnly(2026, 7, 20), CancellationToken.None);
        var callsAfterFirst = invoices.UpdateStatusCalls;

        var executed = await executor.ExecuteDueAsync(new DateOnly(2026, 7, 21), CancellationToken.None);

        Assert.Equal(0, executed);
        Assert.Equal(callsAfterFirst, invoices.UpdateStatusCalls);
    }

    private async Task<PaymentResponse> ScheduleAsync(string billerId, string invoiceId, DateOnly scheduledFor)
    {
        // Mirror the controller's persist-before-mark for a scheduled payment: Pending row, then
        // the invoice due->scheduled transition, then mark the payment Scheduled.
        var payment = new PaymentResponse(
            PaymentId: Guid.NewGuid().ToString(),
            BillerId: billerId,
            InvoiceId: invoiceId,
            PayerAccountId: null,
            Method: "ach",
            AmountCents: 5000,
            FeeCents: 150,
            TotalCents: 5150,
            Confirmation: "PRONTO-TEST01",
            Status: PaymentStatus.Pending,
            ScheduledFor: scheduledFor,
            ReceiptMessage: "Thanks!",
            CreatedAt: DateTimeOffset.UtcNow);
        await store.CreatePendingAsync(payment, idempotencyKey: null, CancellationToken.None);
        await invoices.UpdateStatusAsync(
            billerId, invoiceId, new UpdateInvoiceStatusRequest(InvoiceStatus.Scheduled, payment.PaymentId), CancellationToken.None);
        var scheduled = payment with { Status = PaymentStatus.Scheduled };
        await store.UpdateAsync(scheduled, CancellationToken.None);
        return scheduled;
    }
}
