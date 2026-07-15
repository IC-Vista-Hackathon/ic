namespace Pronto.Payment.Api.Scheduling;

/// <summary>
/// Configuration for the scheduled-payment executor. Bound from the <c>Scheduling</c> section
/// (env <c>Scheduling__Enabled</c>, <c>Scheduling__PollIntervalSeconds</c>).
/// </summary>
public sealed class SchedulingOptions
{
    public const string SectionName = "Scheduling";

    /// <summary>When false the background worker never runs (executor is still unit-testable).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often the worker sweeps for due scheduled payments.</summary>
    public int PollIntervalSeconds { get; set; } = 60;
}
