using System.Text.Json.Serialization;

namespace Pronto.Payment.Api.Assurance;

/// <summary>
/// Stable codes for a reconciliation invariant violation. Each maps to a distinct alert-worthy
/// divergence between what the payer experience could have claimed and what the store actually
/// holds.
/// </summary>
public static class ReconciliationFindingCodes
{
    /// <summary>A payment stuck in <c>pending</c> longer than the orphan threshold.</summary>
    public const string OrphanedPending = "orphaned_pending";

    /// <summary>A finalized (settled/scheduled) payment with no confirmation code.</summary>
    public const string SettledWithoutConfirmation = "settled_without_confirmation";

    /// <summary>The same confirmation code appears on more than one payment record.</summary>
    public const string DuplicateConfirmation = "duplicate_confirmation";

    /// <summary>A record whose total is inconsistent with its amount + fee.</summary>
    public const string TotalMismatch = "total_mismatch";

    /// <summary>A UI-claimed confirmation with no corresponding settled payment record.</summary>
    public const string ConfirmationWithoutRecord = "confirmation_without_record";
}

/// <summary>A single reconciliation invariant violation.</summary>
public sealed record ReconciliationFinding(
    string Code,
    string Detail,
    string? PaymentId = null,
    string? BillerId = null,
    string? Confirmation = null);

/// <summary>
/// Input to a reconciliation pass. <see cref="ClaimedConfirmations"/> are confirmation codes the
/// payer experience claims it displayed as confirmed; each must map to exactly one settled record
/// or it is flagged as a hallucinated confirmation. Optional — omit to run store-only invariants.
/// </summary>
public sealed record ReconciliationRequest(
    IReadOnlyList<string>? ClaimedConfirmations = null);

/// <summary>
/// Structured result of a reconciliation pass. <see cref="Ok"/> is true only when no invariant is
/// violated; a non-empty <see cref="Findings"/> list is the alert-worthy signal. Canary payments
/// are counted separately and excluded from the genuine-traffic checks.
/// </summary>
public sealed record ReconciliationResult(
    bool Ok,
    int TotalRecords,
    int SettledRecords,
    int PendingRecords,
    int FailedRecords,
    int CanaryRecords,
    IReadOnlyList<ReconciliationFinding> Findings)
{
    [JsonIgnore]
    public bool HasDivergence => !Ok;
}
