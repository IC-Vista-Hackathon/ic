using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Pronto.Payment.Api.Assurance;

/// <summary>
/// Telemetry for the post-publish assurance layer (synthetic canary payments + ledger
/// reconciliation). Kept separate from <see cref="Workflow.PaymentTelemetry"/> so assurance
/// signals — which are alert-worthy on divergence — are easy to isolate in metrics/logs. Azure
/// Monitor collects this meter/source automatically via <c>UseAzureMonitor</c>.
/// </summary>
public static class AssuranceTelemetry
{
    public const string SourceName = "Pronto.Payment.Assurance";
    public const string MeterName = "Pronto.Payment.Assurance";

    public static readonly ActivitySource Source = new(SourceName);
    public static readonly Meter Meter = new(MeterName);

    /// <summary>Number of canary payments attempted, tagged by <c>settled</c>.</summary>
    public static readonly Counter<long> CanaryRuns = Meter.CreateCounter<long>("ic.payment.canary.runs");

    /// <summary>Canary payments that failed to settle — an alert-worthy signal.</summary>
    public static readonly Counter<long> CanaryFailures = Meter.CreateCounter<long>("ic.payment.canary.failures");

    /// <summary>Number of reconciliation passes, tagged by <c>ok</c>.</summary>
    public static readonly Counter<long> ReconciliationRuns = Meter.CreateCounter<long>("ic.payment.reconciliation.runs");

    /// <summary>Reconciliation invariant violations, tagged by finding <c>code</c> — alert-worthy.</summary>
    public static readonly Counter<long> ReconciliationFindings =
        Meter.CreateCounter<long>("ic.payment.reconciliation.findings");
}
