namespace Pronto.Payment.Api;

/// <summary>
/// Tunables for the durable payment workflow and the scheduled-payment processor. Bound from the
/// <c>PaymentProcessing</c> config section (env <c>PaymentProcessing__PollIntervalSeconds</c>,
/// …). Exposed so a parent host can configure the Payment Service without editing its startup.
/// </summary>
public sealed class PaymentProcessingOptions
{
    public const string SectionName = "PaymentProcessing";

    /// <summary>Run the background scheduled-payment processor. Default true.</summary>
    public bool SchedulerEnabled { get; set; } = true;

    /// <summary>How often the processor polls for due/stranded payments. Floored at 1s.</summary>
    public int PollIntervalSeconds { get; set; } = 15;

    /// <summary>Exclusive lease a processor holds while finalizing one payment.</summary>
    public int LeaseSeconds { get; set; } = 60;

    /// <summary>
    /// How long a payment may sit in <c>pending</c> before the processor treats it as stranded by
    /// a crash and recovers it. Keeps the normal (sub-second) create path from being reclaimed
    /// mid-flight.
    /// </summary>
    public int PendingRecoveryGraceSeconds { get; set; } = 120;

    /// <summary>Upper bound (days from today) on how far out a payment may be scheduled.</summary>
    public int MaxScheduleDays { get; set; } = 365;
}
