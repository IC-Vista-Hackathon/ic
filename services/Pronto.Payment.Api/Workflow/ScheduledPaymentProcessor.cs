using System.Diagnostics;
using Pronto.Payment.Api.Domain;
using Pronto.Payment.Api.Storage;
using Pronto.ServiceDefaults.Errors;
using Microsoft.Extensions.Options;

namespace Pronto.Payment.Api.Workflow;

/// <summary>
/// Durable processor for payments that need finalizing outside the request path: due scheduled
/// payments (<c>scheduled_for</c> reached) and pending payments stranded by a crash. Each pass
/// claims one record with an exclusive lease (so competing instances never double-process),
/// finalizes it through <see cref="PaymentWorkflow"/>, and returns whether it did any work.
/// </summary>
public sealed partial class ScheduledPaymentProcessor
{
    private readonly IPaymentStore store;
    private readonly PaymentWorkflow workflow;
    private readonly TimeProvider clock;
    private readonly PaymentProcessingOptions options;
    private readonly ILogger<ScheduledPaymentProcessor> logger;

    public ScheduledPaymentProcessor(
        IPaymentStore store,
        PaymentWorkflow workflow,
        TimeProvider clock,
        IOptions<PaymentProcessingOptions> options,
        ILogger<ScheduledPaymentProcessor> logger)
    {
        this.store = store;
        this.workflow = workflow;
        this.clock = clock;
        this.options = options.Value;
        this.logger = logger;
    }

    /// <summary>
    /// Claim and finalize a single due/stranded payment. Returns true when a record was claimed
    /// (so a caller can drain by looping until it returns false).
    /// </summary>
    public async Task<bool> ProcessOnceAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var asOf = DateOnly.FromDateTime(now.UtcDateTime);
        var staleBefore = now - TimeSpan.FromSeconds(Math.Max(1, options.PendingRecoveryGraceSeconds));
        var leaseUntil = now + TimeSpan.FromSeconds(Math.Max(1, options.LeaseSeconds));

        var claimed = await store.ClaimDueAsync(asOf, now, staleBefore, leaseUntil, cancellationToken)
            .ConfigureAwait(false);
        if (claimed is null)
        {
            return false;
        }

        PaymentTelemetry.ProcessorClaims.Add(1, new KeyValuePair<string, object?>("lifecycle", claimed.Lifecycle.ToString()));
        LogClaimed(logger, claimed.PaymentId, claimed.BillerId, claimed.Lifecycle, Activity.Current?.TraceId.ToString());

        try
        {
            // Recover a stranded pending payment by completing its initial transition; settle a
            // due scheduled payment. Both are bound to the originating payment id and idempotent.
            _ = claimed.Lifecycle switch
            {
                PaymentLifecycle.Pending => await workflow.DriveInitialAsync(claimed, cancellationToken).ConfigureAwait(false),
                PaymentLifecycle.Scheduled => await workflow.SettleScheduledAsync(claimed, cancellationToken).ConfigureAwait(false),
                _ => await ReleaseAsync(claimed, cancellationToken).ConfigureAwait(false),
            };
        }
        catch (ServiceException exception)
        {
            // The workflow already marked the payment failed; the invoice refused it. Not retryable.
            LogNotRetryable(logger, claimed.PaymentId, claimed.BillerId, exception.Code, Activity.Current?.TraceId.ToString());
        }
        catch (Exception exception)
        {
            // Transient failure: drop the lease so a later pass retries.
            PaymentTelemetry.ProcessorErrors.Add(1);
            LogProcessError(logger, claimed.PaymentId, claimed.BillerId, Activity.Current?.TraceId.ToString(), exception);
            await ReleaseAsync(claimed, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    private async Task<PaymentRecord> ReleaseAsync(PaymentRecord record, CancellationToken cancellationToken)
    {
        var released = record with { LeaseUntil = null, UpdatedAt = clock.GetUtcNow() };
        return await store.SaveAsync(released, cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(4300, LogLevel.Information, "Processor claimed payment {PaymentId} for biller {BillerId} ({Lifecycle}); trace {TraceId}")]
    private static partial void LogClaimed(ILogger logger, string paymentId, string billerId, PaymentLifecycle lifecycle, string? traceId);

    [LoggerMessage(4301, LogLevel.Warning, "Processor abandoned payment {PaymentId} for biller {BillerId}: {Reason}; trace {TraceId}")]
    private static partial void LogNotRetryable(ILogger logger, string paymentId, string billerId, string reason, string? traceId);

    [LoggerMessage(4302, LogLevel.Error, "Processor failed to finalize payment {PaymentId} for biller {BillerId}; trace {TraceId}")]
    private static partial void LogProcessError(ILogger logger, string paymentId, string billerId, string? traceId, Exception exception);
}
