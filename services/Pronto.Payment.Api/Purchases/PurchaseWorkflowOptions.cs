using System.ComponentModel.DataAnnotations;

namespace Pronto.Payment.Api.Purchases;

/// <summary>
/// Tunables for the recoverable purchase-completion workflow. Bound from the <c>Purchases</c>
/// config section. The synchronous request path always attempts completion once; the durable
/// retry drainer is opt-in (<see cref="BackgroundCompletionEnabled"/>) so a parent host can
/// enable it without touching Program.cs.
/// </summary>
public sealed class PurchaseWorkflowOptions
{
    public const string SectionName = "Purchases";

    /// <summary>Run the background outbox drainer that retries failed completions.</summary>
    public bool BackgroundCompletionEnabled { get; set; }

    /// <summary>Delay between drainer passes.</summary>
    [Range(typeof(TimeSpan), "00:00:00.100", "01:00:00")]
    public TimeSpan DrainInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Maximum pending completions claimed per drainer pass.</summary>
    [Range(1, 1000)]
    public int DrainBatchSize { get; set; } = 50;
}
