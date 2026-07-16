using Pronto.Payment.Api.Domain;
using Pronto.Payment.Api.Storage;
using Microsoft.Extensions.Options;

namespace Pronto.Payment.Api.Assurance;

/// <summary>
/// Ledger reconciliation over the payment store. Verifies the money invariants that a one-time
/// pre-publish gate cannot: every finalized confirmation maps to exactly one settled
/// <see cref="PaymentRecord"/>, no confirmation is claimed without a backing record, no payment is
/// stranded in <c>pending</c> past the orphan threshold, and every record's total is internally
/// consistent with its amount + fee. Canary payments are counted but excluded from genuine-traffic
/// checks (unless configured otherwise) so a synthetic settlement never masks or manufactures a
/// divergence. Emits a structured <see cref="ReconciliationResult"/> plus metrics/logs; a non-empty
/// findings list is the alert-worthy signal.
/// </summary>
public sealed partial class PaymentReconciliationService
{
    private readonly IPaymentStore store;
    private readonly TimeProvider clock;
    private readonly AssuranceOptions options;
    private readonly ILogger<PaymentReconciliationService> logger;

    public PaymentReconciliationService(
        IPaymentStore store,
        TimeProvider clock,
        IOptions<AssuranceOptions> options,
        ILogger<PaymentReconciliationService> logger)
    {
        this.store = store;
        this.clock = clock;
        this.options = options.Value;
        this.logger = logger;
    }

    /// <summary>
    /// Run one reconciliation pass, optionally scoped to a single biller. When
    /// <paramref name="request"/> carries claimed confirmations, each is checked against a settled
    /// record.
    /// </summary>
    public async Task<ReconciliationResult> ReconcileAsync(
        string? billerId,
        ReconciliationRequest? request,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var orphanCutoff = now - TimeSpan.FromSeconds(Math.Max(1, options.OrphanedPendingThresholdSeconds));

        var findings = new List<ReconciliationFinding>();
        var confirmationOwners = new Dictionary<string, List<PaymentRecord>>(StringComparer.Ordinal);

        int total = 0, settled = 0, pending = 0, failed = 0, canary = 0;

        await foreach (var record in store.EnumerateAsync(billerId, cancellationToken).ConfigureAwait(false))
        {
            total++;
            if (record.IsCanary)
            {
                canary++;
                if (!options.IncludeCanariesInReconciliation)
                {
                    continue;
                }
            }

            switch (record.Lifecycle)
            {
                case PaymentLifecycle.Pending:
                    pending++;
                    if (record.UpdatedAt <= orphanCutoff)
                    {
                        findings.Add(new ReconciliationFinding(
                            ReconciliationFindingCodes.OrphanedPending,
                            $"payment stranded in pending since {record.UpdatedAt:O}",
                            record.PaymentId,
                            record.BillerId));
                    }

                    break;
                case PaymentLifecycle.Failed:
                    failed++;
                    break;
                default:
                    settled++;
                    if (string.IsNullOrWhiteSpace(record.Confirmation))
                    {
                        findings.Add(new ReconciliationFinding(
                            ReconciliationFindingCodes.SettledWithoutConfirmation,
                            $"{record.Lifecycle} payment has no confirmation code",
                            record.PaymentId,
                            record.BillerId));
                    }

                    break;
            }

            CheckTotal(record, findings);

            // Confirmations are only client-visible once finalized; a pending record's provisional
            // code is not a claimable confirmation, so it is excluded from uniqueness/mapping.
            if (record.IsFinalized && !string.IsNullOrWhiteSpace(record.Confirmation))
            {
                if (!confirmationOwners.TryGetValue(record.Confirmation, out var owners))
                {
                    owners = [];
                    confirmationOwners[record.Confirmation] = owners;
                }

                owners.Add(record);
            }
        }

        foreach (var (confirmation, owners) in confirmationOwners)
        {
            if (owners.Count > 1)
            {
                findings.Add(new ReconciliationFinding(
                    ReconciliationFindingCodes.DuplicateConfirmation,
                    $"confirmation is claimed by {owners.Count} payment records",
                    owners[0].PaymentId,
                    owners[0].BillerId,
                    confirmation));
            }
        }

        CheckClaimedConfirmations(request, confirmationOwners, findings);

        var ok = findings.Count == 0;
        var result = new ReconciliationResult(
            ok, total, settled, pending, failed, canary, findings);

        EmitTelemetry(billerId, result);
        return result;
    }

    private static void CheckTotal(PaymentRecord record, List<ReconciliationFinding> findings)
    {
        // total is server-computed as either amount (biller absorbs the fee) or amount + fee
        // (payer pays the fee); anything else means the stored total was tampered or corrupted.
        var consistent = record.AmountCents >= 0
            && record.FeeCents >= 0
            && (record.TotalCents == record.AmountCents
                || record.TotalCents == record.AmountCents + record.FeeCents);
        if (!consistent)
        {
            findings.Add(new ReconciliationFinding(
                ReconciliationFindingCodes.TotalMismatch,
                $"total {record.TotalCents} inconsistent with amount {record.AmountCents} + fee {record.FeeCents}",
                record.PaymentId,
                record.BillerId));
        }
    }

    private static void CheckClaimedConfirmations(
        ReconciliationRequest? request,
        Dictionary<string, List<PaymentRecord>> confirmationOwners,
        List<ReconciliationFinding> findings)
    {
        if (request?.ClaimedConfirmations is not { Count: > 0 } claimed)
        {
            return;
        }

        foreach (var confirmation in claimed)
        {
            if (string.IsNullOrWhiteSpace(confirmation))
            {
                continue;
            }

            if (!confirmationOwners.ContainsKey(confirmation))
            {
                findings.Add(new ReconciliationFinding(
                    ReconciliationFindingCodes.ConfirmationWithoutRecord,
                    "UI-claimed confirmation has no settled payment record",
                    Confirmation: confirmation));
            }
        }
    }

    private void EmitTelemetry(string? billerId, ReconciliationResult result)
    {
        AssuranceTelemetry.ReconciliationRuns.Add(
            1, new KeyValuePair<string, object?>("ok", result.Ok));
        foreach (var group in result.Findings.GroupBy(finding => finding.Code, StringComparer.Ordinal))
        {
            AssuranceTelemetry.ReconciliationFindings.Add(
                group.Count(), new KeyValuePair<string, object?>("code", group.Key));
        }

        if (result.Ok)
        {
            LogReconciliationOk(logger, billerId ?? "*", result.TotalRecords, result.SettledRecords);
        }
        else
        {
            LogReconciliationDivergence(
                logger, billerId ?? "*", result.Findings.Count, result.TotalRecords);
        }
    }

    [LoggerMessage(4400, LogLevel.Information,
        "Reconciliation passed for biller {BillerId}: {TotalRecords} records, {SettledRecords} settled")]
    private static partial void LogReconciliationOk(
        ILogger logger, string billerId, int totalRecords, int settledRecords);

    [LoggerMessage(4401, LogLevel.Warning,
        "Reconciliation DIVERGENCE for biller {BillerId}: {FindingCount} findings over {TotalRecords} records")]
    private static partial void LogReconciliationDivergence(
        ILogger logger, string billerId, int findingCount, int totalRecords);
}
