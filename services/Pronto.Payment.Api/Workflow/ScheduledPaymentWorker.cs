using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Pronto.Payment.Api.Workflow;

/// <summary>
/// Hosts <see cref="ScheduledPaymentProcessor"/> as a background service: each interval it drains
/// all currently-due/stranded payments, then sleeps. Resolves the processor from a fresh DI scope
/// per pass so it can depend on scoped services (the typed Invoice HTTP client). Kept thin so the
/// processor stays unit-testable without a host.
/// </summary>
public sealed partial class ScheduledPaymentWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<PaymentProcessingOptions> options,
    ILogger<ScheduledPaymentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.PollIntervalSeconds));
        LogReady(logger, interval.TotalSeconds, Activity.Current?.TraceId.ToString());

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<ScheduledPaymentProcessor>();
                while (await processor.ProcessOnceAsync(stoppingToken).ConfigureAwait(false))
                {
                    // drain everything currently due before sleeping
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogPollError(logger, Activity.Current?.TraceId.ToString(), exception);
            }

            try
            {
                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        LogStopped(logger, Activity.Current?.TraceId.ToString());
    }

    [LoggerMessage(4310, LogLevel.Information, "Scheduled-payment processor ready; polling every {IntervalSeconds}s; trace {TraceId}")]
    private static partial void LogReady(ILogger logger, double intervalSeconds, string? traceId);

    [LoggerMessage(4311, LogLevel.Information, "Scheduled-payment processor stopped; trace {TraceId}")]
    private static partial void LogStopped(ILogger logger, string? traceId);

    [LoggerMessage(4312, LogLevel.Error, "Scheduled-payment processor poll failed; trace {TraceId}")]
    private static partial void LogPollError(ILogger logger, string? traceId, Exception exception);
}
