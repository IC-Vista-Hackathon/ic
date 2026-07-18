namespace Pronto.Payment.Api.Assurance;

/// <summary>
/// Tunables for the post-publish assurance layer. Bound from the <c>Assurance</c> config section
/// (env <c>Assurance__ReconciliationEnabled</c>, …). The background worker is opt-in so it never
/// runs cross-partition scans in environments that don't want it; the endpoints are always
/// available (nonprod-gated) for on-demand triggering.
/// </summary>
public sealed class AssuranceOptions
{
    public const string SectionName = "Assurance";

    /// <summary>Expose the triggerable assurance endpoints. Nonprod only; default true.</summary>
    public bool EndpointsEnabled { get; set; } = true;

    /// <summary>Run reconciliation continuously in the background. Default false (opt-in).</summary>
    public bool ReconciliationEnabled { get; set; }

    /// <summary>Run the synthetic canary continuously in the background. Default false (opt-in).</summary>
    public bool CanaryEnabled { get; set; }

    /// <summary>How often the background worker runs its enabled passes. Floored at 5s.</summary>
    public int IntervalSeconds { get; set; } = 300;

    /// <summary>
    /// How long a payment may sit in <c>pending</c> before reconciliation treats it as an orphan.
    /// Well above the create path's sub-second finalization and the processor's recovery grace.
    /// </summary>
    public int OrphanedPendingThresholdSeconds { get; set; } = 900;

    /// <summary>Include canary payments in genuine-traffic reconciliation. Default false.</summary>
    public bool IncludeCanariesInReconciliation { get; set; }
}
