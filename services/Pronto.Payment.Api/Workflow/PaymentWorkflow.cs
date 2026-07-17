using System.Diagnostics;
using System.Diagnostics.Metrics;
using Pronto.Invoice.Contracts.V1.Invoices;
using Pronto.Payment.Api.Clients;
using Pronto.Payment.Api.Domain;
using Pronto.Payment.Api.Storage;
using Pronto.ServiceDefaults.Errors;

namespace Pronto.Payment.Api.Workflow;

/// <summary>
/// The recoverable payment state machine shared by the API (create/resume) and the background
/// processor (recover/settle). Ordering is payment-first: a pending <see cref="PaymentRecord"/> is
/// always durable <em>before</em> the invoice transition, and the invoice transition is bound to
/// the payment id, so every partial failure is recoverable and idempotent:
/// <list type="bullet">
/// <item>crash after the pending write, before the invoice flip → still <c>due</c>; re-asserting
/// the same transition completes it.</item>
/// <item>crash after the invoice flip, before finalize → the invoice already carries this payment
/// id, so re-asserting is an idempotent no-op that lets us finalize.</item>
/// </list>
/// No path can leave a paid/scheduled invoice with no payment (the orphan the audit targets).
/// </summary>
public sealed partial class PaymentWorkflow
{
    private readonly IPaymentStore store;
    private readonly IInvoiceClient invoices;
    private readonly TimeProvider clock;
    private readonly ILogger<PaymentWorkflow> logger;

    public PaymentWorkflow(
        IPaymentStore store,
        IInvoiceClient invoices,
        TimeProvider clock,
        ILogger<PaymentWorkflow> logger)
    {
        this.store = store;
        this.invoices = invoices;
        this.clock = clock;
        this.logger = logger;
    }

    /// <summary>
    /// Drive a pending record to its finalized state by asserting the <em>initial</em> invoice
    /// transition (<c>due→paid</c> for immediate, <c>due→scheduled</c> for scheduled), bound to the
    /// payment id. If the invoice refuses (already paid, schedule locked, invalid) the payment is
    /// marked <see cref="PaymentLifecycle.Failed"/> and the invoice conflict is rethrown.
    /// Idempotent: a record already finalized is returned unchanged.
    /// </summary>
    public async Task<PaymentRecord> DriveInitialAsync(PaymentRecord record, CancellationToken cancellationToken)
    {
        if (record.IsFinalized)
        {
            return record;
        }

        PaymentLifecycle finalLifecycle;
        if (record.ScheduledFor is not null && !record.IsInstallment)
        {
            // A one-time scheduled full payment: reserve the invoice now (due→scheduled) and settle
            // it on its date. Unchanged from before partial/installment support.
            await AssertTransitionOrFailAsync(record, InvoiceStatus.Scheduled, cancellationToken).ConfigureAwait(false);
            finalLifecycle = PaymentLifecycle.Scheduled;
        }
        else
        {
            // Immediate payment (full or partial). Only the payment that clears the outstanding
            // balance settles the invoice to paid; a partial that leaves a balance records the
            // payment and leaves the invoice due for the next one.
            if (await SettlesBalanceAsync(record, cancellationToken).ConfigureAwait(false))
            {
                await AssertTransitionOrFailAsync(record, InvoiceStatus.Paid, cancellationToken).ConfigureAwait(false);
            }

            finalLifecycle = PaymentLifecycle.Succeeded;
        }

        var finalized = record with
        {
            Lifecycle = finalLifecycle,
            LeaseUntil = null,
            FailureReason = null,
            UpdatedAt = clock.GetUtcNow(),
        };
        var saved = await store.SaveAsync(finalized, cancellationToken).ConfigureAwait(false);
        PaymentTelemetry.Finalized.Add(1, new KeyValuePair<string, object?>("lifecycle", finalLifecycle.ToString()));
        LogFinalized(logger, saved.PaymentId, saved.BillerId, finalLifecycle, Activity.Current?.TraceId.ToString());
        return saved;
    }

    /// <summary>
    /// Settle a due scheduled payment: assert <c>scheduled→paid</c> bound to the originating
    /// payment id, then mark the payment succeeded. Idempotent on replay.
    /// </summary>
    public async Task<PaymentRecord> SettleScheduledAsync(PaymentRecord record, CancellationToken cancellationToken)
    {
        if (record.Lifecycle != PaymentLifecycle.Scheduled)
        {
            return record;
        }

        // An installment is a scheduled partial: only the one that clears the remaining balance
        // settles the invoice to paid; earlier installments succeed without touching the invoice.
        // A one-time scheduled full payment always settles scheduled→paid (unchanged).
        if (!record.IsInstallment || await SettlesBalanceAsync(record, cancellationToken).ConfigureAwait(false))
        {
            await AssertTransitionOrFailAsync(record, InvoiceStatus.Paid, cancellationToken).ConfigureAwait(false);
        }

        var settled = record with
        {
            Lifecycle = PaymentLifecycle.Succeeded,
            LeaseUntil = null,
            FailureReason = null,
            UpdatedAt = clock.GetUtcNow(),
        };
        var saved = await store.SaveAsync(settled, cancellationToken).ConfigureAwait(false);
        PaymentTelemetry.Finalized.Add(1, new KeyValuePair<string, object?>("lifecycle", "scheduled_settled"));
        LogFinalized(logger, saved.PaymentId, saved.BillerId, PaymentLifecycle.Succeeded, Activity.Current?.TraceId.ToString());
        return saved;
    }

    /// <summary>
    /// True when finalizing <paramref name="record"/> brings the invoice's <em>settled</em> total
    /// (succeeded payments only) up to its full amount — i.e. this is the payment that pays it off.
    /// The invoice amount is the server's authority; the client-requested amount never decides this.
    /// </summary>
    private async Task<bool> SettlesBalanceAsync(PaymentRecord record, CancellationToken cancellationToken)
    {
        var invoice = await invoices.GetAsync(record.BillerId, record.InvoiceId, cancellationToken)
            .ConfigureAwait(false);
        if (invoice is null)
        {
            // No invoice to look up: fall back to attempting the paid transition (it will fail
            // loudly rather than silently leave a partial in an inconsistent state).
            return true;
        }

        var settledElsewhere = (await store
            .ListAsync(record.BillerId, payerAccountId: null, invoiceId: record.InvoiceId, cancellationToken)
            .ConfigureAwait(false))
            .Where(other =>
                other.Lifecycle == PaymentLifecycle.Succeeded
                && !string.Equals(other.PaymentId, record.PaymentId, StringComparison.Ordinal))
            .Sum(other => other.AmountCents);

        return settledElsewhere + record.AmountCents >= invoice.AmountCents;
    }

    private async Task AssertTransitionOrFailAsync(
        PaymentRecord record, InvoiceStatus target, CancellationToken cancellationToken)
    {
        try
        {
            await invoices.UpdateStatusAsync(
                record.BillerId,
                record.InvoiceId,
                new UpdateInvoiceStatusRequest(target, record.PaymentId),
                cancellationToken).ConfigureAwait(false);
        }
        catch (ServiceException exception)
        {
            var failed = record with
            {
                Lifecycle = PaymentLifecycle.Failed,
                FailureReason = exception.Code,
                LeaseUntil = null,
                UpdatedAt = clock.GetUtcNow(),
            };
            await store.SaveAsync(failed, cancellationToken).ConfigureAwait(false);
            PaymentTelemetry.Finalized.Add(1, new KeyValuePair<string, object?>("lifecycle", "failed"));
            LogAbandoned(logger, record.PaymentId, record.BillerId, exception.Code, Activity.Current?.TraceId.ToString());
            throw;
        }
    }

    [LoggerMessage(4200, LogLevel.Information, "Finalized payment {PaymentId} for biller {BillerId} to {Lifecycle}; trace {TraceId}")]
    private static partial void LogFinalized(ILogger logger, string paymentId, string billerId, PaymentLifecycle lifecycle, string? traceId);

    [LoggerMessage(4201, LogLevel.Warning, "Abandoned payment {PaymentId} for biller {BillerId}: invoice refused with {Reason}; trace {TraceId}")]
    private static partial void LogAbandoned(ILogger logger, string paymentId, string billerId, string reason, string? traceId);
}

/// <summary>Telemetry for payment finalization and the scheduled processor.</summary>
public static class PaymentTelemetry
{
    public const string SourceName = "Pronto.Payment";
    public const string MeterName = "Pronto.Payment";
    public static readonly ActivitySource Source = new(SourceName);
    public static readonly Meter Meter = new(MeterName);
    public static readonly Counter<long> Finalized = Meter.CreateCounter<long>("ic.payment.finalized");
    public static readonly Counter<long> ProcessorClaims = Meter.CreateCounter<long>("ic.payment.processor.claims");
    public static readonly Counter<long> ProcessorErrors = Meter.CreateCounter<long>("ic.payment.processor.errors");
}
