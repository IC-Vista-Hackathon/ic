using Pronto.Payment.Api.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Pronto.Payment.Api.Purchases;

/// <summary>
/// Durable retry drainer: periodically completes purchases whose BillerAccount transition has
/// not yet succeeded. Opt-in via <see cref="PurchaseWorkflowOptions.BackgroundCompletionEnabled"/>
/// so a parent host can own the recovery cadence without editing the service host.
/// </summary>
public sealed partial class PurchaseCompletionProcessor(
    IPurchaseCompletionOutbox outbox,
    PurchaseCompletionRunner runner,
    IOptions<PurchaseWorkflowOptions> options,
    ILogger<PurchaseCompletionProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        LogDrainerReady(logger, settings.DrainInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DrainOnceAsync(settings.DrainBatchSize, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogDrainError(logger, exception);
            }

            try
            {
                await Task.Delay(settings.DrainInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>Runs a single drain pass. Exposed for tests and manual recovery triggers.</summary>
    public async Task<int> DrainOnceAsync(int batchSize, CancellationToken cancellationToken)
    {
        var pending = await outbox.ListPendingCompletionsAsync(batchSize, cancellationToken)
            .ConfigureAwait(false);
        var completed = 0;

        foreach (var completion in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var paid = await runner.TryCompleteAsync(completion, cancellationToken).ConfigureAwait(false);
            if (paid is not null)
            {
                completed++;
            }
        }

        return completed;
    }

    [LoggerMessage(410, LogLevel.Information, "Purchase completion drainer ready; interval {Interval}")]
    private static partial void LogDrainerReady(ILogger logger, TimeSpan interval);

    [LoggerMessage(411, LogLevel.Error, "Purchase completion drain pass failed")]
    private static partial void LogDrainError(ILogger logger, Exception exception);
}
