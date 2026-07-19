using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Pronto.Payment.Api.Assurance;

/// <summary>
/// Hosts continuous post-publish assurance: each interval it runs the enabled passes (ledger
/// reconciliation and/or the synthetic canary) so proof that live payments settle keeps flowing
/// rather than rotting after a one-time pre-publish gate. Both passes are opt-in via
/// <see cref="AssuranceOptions"/>; results and divergences surface through
/// <see cref="AssuranceTelemetry"/> (App Insights metrics/logs). Resolves scoped services from a
/// fresh scope per pass so it can depend on the scoped payment workflow.
/// </summary>
public sealed partial class AssuranceWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<AssuranceOptions> options,
    ILogger<AssuranceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = options.Value;
        if (!config.ReconciliationEnabled && !config.CanaryEnabled)
        {
            LogDisabled(logger);
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(5, config.IntervalSeconds));
        LogReady(logger, interval.TotalSeconds, config.ReconciliationEnabled, config.CanaryEnabled);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunPassAsync(config, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogPassError(logger, Activity.Current?.TraceId.ToString(), exception);
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

        LogStopped(logger);
    }

    private async Task RunPassAsync(AssuranceOptions config, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        if (config.CanaryEnabled)
        {
            var source = scope.ServiceProvider.GetRequiredService<ICanaryTargetSource>();
            var runner = scope.ServiceProvider.GetRequiredService<CanaryPaymentRunner>();
            var targets = await source.GetTargetsAsync(cancellationToken).ConfigureAwait(false);
            var result = await runner.RunAllAsync(targets, cancellationToken).ConfigureAwait(false);
            LogCanaryPass(logger, result.TargetCount, result.Outcomes.Count(o => o.Settled), result.Ok);
        }

        if (config.ReconciliationEnabled)
        {
            var reconciler = scope.ServiceProvider.GetRequiredService<PaymentReconciliationService>();
            var result = await reconciler.ReconcileAsync(null, null, cancellationToken).ConfigureAwait(false);
            LogReconciliationPass(logger, result.TotalRecords, result.Findings.Count, result.Ok);
        }
    }

    [LoggerMessage(4420, LogLevel.Information, "Assurance worker disabled (no passes enabled)")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(4421, LogLevel.Information,
        "Assurance worker ready; every {IntervalSeconds}s; reconciliation={Reconciliation} canary={Canary}")]
    private static partial void LogReady(
        ILogger logger, double intervalSeconds, bool reconciliation, bool canary);

    [LoggerMessage(4422, LogLevel.Information,
        "Assurance canary pass: {Settled}/{TargetCount} settled; ok={Ok}")]
    private static partial void LogCanaryPass(ILogger logger, int targetCount, int settled, bool ok);

    [LoggerMessage(4423, LogLevel.Information,
        "Assurance reconciliation pass: {TotalRecords} records, {FindingCount} findings; ok={Ok}")]
    private static partial void LogReconciliationPass(
        ILogger logger, int totalRecords, int findingCount, bool ok);

    [LoggerMessage(4424, LogLevel.Error, "Assurance worker pass failed; trace {TraceId}")]
    private static partial void LogPassError(ILogger logger, string? traceId, Exception exception);

    [LoggerMessage(4425, LogLevel.Information, "Assurance worker stopped")]
    private static partial void LogStopped(ILogger logger);
}
