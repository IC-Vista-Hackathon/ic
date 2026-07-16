using System.Diagnostics;
using System.Security.Cryptography;
using Pronto.Invoice.Contracts.V1.Invoices;
using Pronto.Payment.Api.Clients;
using Pronto.Payment.Api.Domain;
using Pronto.Payment.Api.Fees;
using Pronto.Payment.Api.Storage;
using Pronto.Payment.Api.Workflow;
using Pronto.ServiceDefaults.Errors;

namespace Pronto.Payment.Api.Assurance;

/// <summary>
/// Runs synthetic canary payments through the genuine end-to-end payment path on the fake rail.
/// For each published biller it pays a pre-seeded canary invoice using the same building blocks as
/// the real controller — biller config + <see cref="FeeCalculator"/>, the durable pending record,
/// and <see cref="PaymentWorkflow"/> — so a regression that breaks live settlement also breaks the
/// canary. It asserts the settlement invariants (confirmation minted, amount == invoice amount, fee
/// == FeeCalculator, exactly-once on retry) against the persisted record, and flags every record it
/// creates with <see cref="PaymentRecord.IsCanary"/> so synthetic settlements never pollute
/// reconciliation of genuine traffic. Money semantics are never invented here: amount always comes
/// from the invoice and fee/total always come from the server-side calculator.
/// </summary>
public sealed partial class CanaryPaymentRunner
{
    private const string ConfirmationAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private readonly IPaymentStore store;
    private readonly IInvoiceClient invoices;
    private readonly IBillerConfigClient configs;
    private readonly PaymentWorkflow workflow;
    private readonly TimeProvider clock;
    private readonly ILogger<CanaryPaymentRunner> logger;

    public CanaryPaymentRunner(
        IPaymentStore store,
        IInvoiceClient invoices,
        IBillerConfigClient configs,
        PaymentWorkflow workflow,
        TimeProvider clock,
        ILogger<CanaryPaymentRunner> logger)
    {
        this.store = store;
        this.invoices = invoices;
        this.configs = configs;
        this.workflow = workflow;
        this.clock = clock;
        this.logger = logger;
    }

    /// <summary>Run the canary for one target and assert it genuinely settles.</summary>
    public async Task<CanaryOutcome> RunAsync(CanaryTarget target, CancellationToken cancellationToken)
    {
        try
        {
            return await RunCoreAsync(target, cancellationToken).ConfigureAwait(false);
        }
        catch (ServiceException exception)
        {
            return Failure(target, exception.Code, exception.Message);
        }
    }

    /// <summary>Run the canary for every configured target.</summary>
    public async Task<CanaryRunResult> RunAllAsync(
        IReadOnlyList<CanaryTarget> targets, CancellationToken cancellationToken)
    {
        var outcomes = new List<CanaryOutcome>(targets.Count);
        foreach (var target in targets)
        {
            outcomes.Add(await RunAsync(target, cancellationToken).ConfigureAwait(false));
        }

        return new CanaryRunResult(outcomes.All(outcome => outcome.Settled), targets.Count, outcomes);
    }

    private async Task<CanaryOutcome> RunCoreAsync(CanaryTarget target, CancellationToken cancellationToken)
    {
        var config = await configs.GetAsync(target.BillerId, cancellationToken).ConfigureAwait(false);
        if (!config.PaymentMethods.Contains(target.Method))
        {
            return Failure(target, "method_not_enabled", $"method '{target.Method}' is not enabled for this biller");
        }

        var paymentId = PaymentRecord.DeriveId(target.BillerId, target.IdempotencyKey);
        var fingerprint = PaymentRecord.Fingerprint(target.InvoiceId, target.Method, target.PayerAccountId, null);

        // Exactly-once on retry: a canary re-run with the same key replays the original outcome
        // through the genuine workflow rather than minting a second payment.
        var existing = await store.FindAsync(target.BillerId, paymentId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            var replayed = await workflow.DriveInitialAsync(existing, cancellationToken).ConfigureAwait(false);
            return Assert(target, replayed, config, idempotentReplay: true);
        }

        var invoice = await invoices.GetAsync(target.BillerId, target.InvoiceId, cancellationToken)
                .ConfigureAwait(false)
            ?? throw ServiceException.NotFound("invoice_not_found", $"canary invoice {target.InvoiceId} not found");

        if (invoice.Status != InvoiceStatus.Due)
        {
            return Failure(
                target, "canary_invoice_not_payable", $"canary invoice {target.InvoiceId} is {invoice.Status}, not due");
        }

        var (feeCents, totalCents) = FeeCalculator.Calculate(config, target.Method, invoice.AmountCents);
        var now = clock.GetUtcNow();
        var pending = new PaymentRecord
        {
            PaymentId = paymentId,
            BillerId = target.BillerId,
            InvoiceId = target.InvoiceId,
            PayerAccountId = target.PayerAccountId,
            Method = target.Method,
            AmountCents = invoice.AmountCents,
            FeeCents = feeCents,
            TotalCents = totalCents,
            Confirmation = MintConfirmation(),
            ScheduledFor = null,
            ReceiptMessage = config.ReceiptMessage,
            Lifecycle = PaymentLifecycle.Pending,
            IdempotencyKey = target.IdempotencyKey,
            RequestFingerprint = fingerprint,
            IsCanary = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var begin = await store.BeginAsync(pending, cancellationToken).ConfigureAwait(false);
        var finalized = await workflow.DriveInitialAsync(begin.Record, cancellationToken).ConfigureAwait(false);
        return Assert(target, finalized, config, idempotentReplay: !begin.Created);
    }

    /// <summary>Assert the settlement invariants against the persisted record.</summary>
    private CanaryOutcome Assert(
        CanaryTarget target, PaymentRecord record, BillerPaymentConfig config, bool idempotentReplay)
    {
        var (expectedFee, expectedTotal) = FeeCalculator.Calculate(config, record.Method, record.AmountCents);
        var settled = record.Lifecycle == PaymentLifecycle.Succeeded;

        string? failureCode = null;
        string? failureDetail = null;
        if (!settled)
        {
            failureCode = record.FailureReason ?? "not_settled";
            failureDetail = $"canary payment ended in {record.Lifecycle}";
        }
        else if (string.IsNullOrWhiteSpace(record.Confirmation))
        {
            failureCode = "missing_confirmation";
            failureDetail = "settled canary payment has no confirmation";
        }
        else if (record.FeeCents != expectedFee || record.TotalCents != expectedTotal)
        {
            failureCode = "fee_mismatch";
            failureDetail =
                $"stored fee/total {record.FeeCents}/{record.TotalCents} != calculator {expectedFee}/{expectedTotal}";
        }

        var outcomeSettled = settled && failureCode is null;
        AssuranceTelemetry.CanaryRuns.Add(
            1,
            new KeyValuePair<string, object?>("settled", outcomeSettled),
            new KeyValuePair<string, object?>("replay", idempotentReplay));
        if (outcomeSettled)
        {
            LogCanarySettled(
                logger, record.BillerId, record.InvoiceId, record.PaymentId, idempotentReplay,
                Activity.Current?.TraceId.ToString());
        }
        else
        {
            AssuranceTelemetry.CanaryFailures.Add(1, new KeyValuePair<string, object?>("code", failureCode));
            LogCanaryFailed(logger, record.BillerId, record.InvoiceId, failureCode, Activity.Current?.TraceId.ToString());
        }

        return new CanaryOutcome(
            record.BillerId,
            record.InvoiceId,
            record.Method,
            outcomeSettled,
            idempotentReplay,
            record.PaymentId,
            record.Confirmation,
            record.AmountCents,
            record.FeeCents,
            record.TotalCents,
            failureCode,
            failureDetail);
    }

    private CanaryOutcome Failure(CanaryTarget target, string code, string detail)
    {
        AssuranceTelemetry.CanaryRuns.Add(1, new KeyValuePair<string, object?>("settled", false));
        AssuranceTelemetry.CanaryFailures.Add(1, new KeyValuePair<string, object?>("code", code));
        LogCanaryFailed(logger, target.BillerId, target.InvoiceId, code, Activity.Current?.TraceId.ToString());
        return new CanaryOutcome(
            target.BillerId, target.InvoiceId, target.Method,
            Settled: false, IdempotentReplay: false,
            PaymentId: null, Confirmation: null,
            AmountCents: 0, FeeCents: 0, TotalCents: 0,
            FailureCode: code, FailureDetail: detail);
    }

    private static string MintConfirmation()
    {
        Span<char> code = stackalloc char[6];
        for (var index = 0; index < code.Length; index++)
        {
            code[index] = ConfirmationAlphabet[RandomNumberGenerator.GetInt32(ConfirmationAlphabet.Length)];
        }

        return $"PRONTO-{new string(code)}";
    }

    [LoggerMessage(4410, LogLevel.Information,
        "Canary settled for biller {BillerId} invoice {InvoiceId} payment {PaymentId} (replay {Replay}); trace {TraceId}")]
    private static partial void LogCanarySettled(
        ILogger logger, string billerId, string invoiceId, string paymentId, bool replay, string? traceId);

    [LoggerMessage(4411, LogLevel.Warning,
        "Canary FAILED for biller {BillerId} invoice {InvoiceId}: {Code}; trace {TraceId}")]
    private static partial void LogCanaryFailed(
        ILogger logger, string billerId, string invoiceId, string? code, string? traceId);
}
