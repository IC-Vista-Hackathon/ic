using System.Diagnostics;
using System.Diagnostics.Metrics;
using Pronto.Invoice.Contracts.V1.Invoices;
using Pronto.Payment.Api.Clients;
using Pronto.Payment.Api.Storage;
using Pronto.Payment.Contracts.V1.Payments;
using Pronto.ServiceDefaults.Errors;
using Microsoft.AspNetCore.Http;

namespace Pronto.Payment.Api.Scheduling;

/// <summary>
/// Executes due <see cref="PaymentStatus.Scheduled"/> payments deterministically from Cosmos
/// state: for each payment whose <c>ScheduledFor</c> has arrived, assert the invoice
/// <c>scheduled -> paid</c> transition then mark the payment <see cref="PaymentStatus.Succeeded"/>.
/// Safe to re-run on restart — a payment is only picked while it is still <c>scheduled</c>, and an
/// invoice already <c>paid</c> (e.g. by this same payment on a prior crashed run) is treated as
/// success so the payment is still finalized.
/// </summary>
public sealed partial class ScheduledPaymentExecutor(
    IPaymentStore store,
    IInvoiceClient invoices,
    ILogger<ScheduledPaymentExecutor> logger)
{
    /// <summary>Execute every scheduled payment due on or before <paramref name="asOf"/>.
    /// Returns the number of payments finalized.</summary>
    public async Task<int> ExecuteDueAsync(DateOnly asOf, CancellationToken cancellationToken)
    {
        using var activity = SchedulingTelemetry.Source.StartActivity("payment.schedule.sweep");
        var due = await store.ListDueScheduledAsync(asOf, cancellationToken).ConfigureAwait(false);
        var executed = 0;
        foreach (var payment in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await TryExecuteAsync(payment, cancellationToken).ConfigureAwait(false))
            {
                executed++;
            }
        }

        return executed;
    }

    private async Task<bool> TryExecuteAsync(PaymentResponse payment, CancellationToken cancellationToken)
    {
        try
        {
            try
            {
                await invoices.UpdateStatusAsync(
                    payment.BillerId,
                    payment.InvoiceId,
                    new UpdateInvoiceStatusRequest(InvoiceStatus.Paid, payment.PaymentId),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (ServiceException exception) when (exception.StatusCode == StatusCodes.Status409Conflict)
            {
                // Invoice already paid (prior crashed run finalized the invoice but not the payment):
                // idempotent — finalize the payment below.
                LogInvoiceAlreadyPaid(logger, payment.PaymentId, payment.BillerId, payment.InvoiceId, Activity.Current?.TraceId.ToString());
            }

            await store.UpdateAsync(
                payment with { Status = PaymentStatus.Succeeded }, cancellationToken).ConfigureAwait(false);
            SchedulingTelemetry.Executed.Add(1);
            LogPaymentExecuted(logger, payment.PaymentId, payment.BillerId, payment.InvoiceId, Activity.Current?.TraceId.ToString());
            return true;
        }
        catch (Exception exception)
        {
            SchedulingTelemetry.Errors.Add(1);
            LogExecuteError(logger, payment.PaymentId, payment.BillerId, payment.InvoiceId, Activity.Current?.TraceId.ToString(), exception);
            return false;
        }
    }

    [LoggerMessage(4200, LogLevel.Information, "Executed scheduled payment {PaymentId} for biller {BillerId}, invoice {InvoiceId}; trace {TraceId}")]
    private static partial void LogPaymentExecuted(ILogger logger, string paymentId, string billerId, string invoiceId, string? traceId);

    [LoggerMessage(4201, LogLevel.Information, "Invoice already paid while executing scheduled payment {PaymentId} for biller {BillerId}, invoice {InvoiceId}; finalizing payment; trace {TraceId}")]
    private static partial void LogInvoiceAlreadyPaid(ILogger logger, string paymentId, string billerId, string invoiceId, string? traceId);

    [LoggerMessage(4900, LogLevel.Error, "Failed to execute scheduled payment {PaymentId} for biller {BillerId}, invoice {InvoiceId}; trace {TraceId}")]
    private static partial void LogExecuteError(ILogger logger, string paymentId, string billerId, string invoiceId, string? traceId, Exception exception);
}

public static class SchedulingTelemetry
{
    public const string SourceName = "Pronto.Payment.Scheduling";
    public const string MeterName = "Pronto.Payment.Scheduling";
    public static readonly ActivitySource Source = new(SourceName);
    public static readonly Meter Meter = new(MeterName);
    public static readonly Counter<long> Executed = Meter.CreateCounter<long>("ic.payment.scheduled.executed");
    public static readonly Counter<long> Errors = Meter.CreateCounter<long>("ic.payment.scheduled.errors");
}
