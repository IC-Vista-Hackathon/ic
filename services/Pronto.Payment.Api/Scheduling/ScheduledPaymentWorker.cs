using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace Pronto.Payment.Api.Scheduling;

/// <summary>
/// Hosted sweep that drives <see cref="ScheduledPaymentExecutor"/> on an interval. Uses the
/// injected <see cref="TimeProvider"/> so "today" is deterministic in tests. A failed sweep is
/// logged and retried next interval — never crashes the host.
/// </summary>
public sealed partial class ScheduledPaymentWorker(
    ScheduledPaymentExecutor executor,
    TimeProvider timeProvider,
    IOptions<SchedulingOptions> options,
    ILogger<ScheduledPaymentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            LogDisabled(logger);
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.PollIntervalSeconds));
        LogReady(logger, interval.TotalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
                await executor.ExecuteDueAsync(today, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogSweepError(logger, Activity.Current?.TraceId.ToString(), exception);
            }

            try
            {
                await Task.Delay(interval, timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    [LoggerMessage(4210, LogLevel.Information, "Scheduled-payment worker ready; sweeping every {IntervalSeconds}s")]
    private static partial void LogReady(ILogger logger, double intervalSeconds);

    [LoggerMessage(4211, LogLevel.Information, "Scheduled-payment worker disabled by configuration")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(4910, LogLevel.Error, "Scheduled-payment sweep failed; trace {TraceId}")]
    private static partial void LogSweepError(ILogger logger, string? traceId, Exception exception);
}
