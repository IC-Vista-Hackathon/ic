using System.Diagnostics;
using System.Diagnostics.Metrics;
using Pronto.BillerExperience.Worker.Persistence;
using Microsoft.Extensions.Options;

namespace Pronto.BillerExperience.Worker;

public sealed partial class PublicationWorker(
    IPublicationRepository repository,
    PublicationProcessor processor,
    IOptions<PublicationOptions> options,
    ILogger<PublicationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var activity = PublicationTelemetry.Source.StartActivity("publication.worker.run");
        var traceId = activity?.TraceId.ToString();
        LogWorkerReady(logger, traceId);
        PublicationTelemetry.WorkerStarts.Add(1);
        var interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.PollIntervalSeconds));

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var deployment = await repository.ClaimNextAsync(stoppingToken);
                    if (deployment is null)
                    {
                        await Task.Delay(interval, stoppingToken);
                        continue;
                    }

                    await processor.ProcessAsync(deployment, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    PublicationTelemetry.WorkerErrors.Add(1, new KeyValuePair<string, object?>("scope", "poll"));
                    LogPollError(logger, Activity.Current?.TraceId.ToString(), exception);
                    await Task.Delay(interval, stoppingToken);
                }
            }
            LogWorkerStopped(logger, traceId);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            LogWorkerStopped(logger, traceId);
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            PublicationTelemetry.WorkerErrors.Add(1, new KeyValuePair<string, object?>("scope", "worker"));
            LogWorkerError(logger, traceId, exception);
            throw;
        }
    }

    [LoggerMessage(1000, LogLevel.Information, "Pronto Biller Experience publication worker is ready; trace {TraceId}")]
    private static partial void LogWorkerReady(ILogger logger, string? traceId);

    [LoggerMessage(1001, LogLevel.Information, "Pronto Biller Experience publication worker stopped; trace {TraceId}")]
    private static partial void LogWorkerStopped(ILogger logger, string? traceId);

    [LoggerMessage(1900, LogLevel.Error, "Pronto Biller Experience publication worker failed; trace {TraceId}")]
    private static partial void LogWorkerError(ILogger logger, string? traceId, Exception exception);

    [LoggerMessage(1901, LogLevel.Error, "Publication polling failed; trace {TraceId}")]
    private static partial void LogPollError(ILogger logger, string? traceId, Exception exception);

}

public static class PublicationTelemetry
{
    public const string SourceName = "Pronto.BillerExperience.Publication";
    public const string MeterName = "Pronto.BillerExperience.Publication";
    public static readonly ActivitySource Source = new(SourceName);
    public static readonly Meter Meter = new(MeterName);
    public static readonly Counter<long> WorkerStarts = Meter.CreateCounter<long>("ic.biller.publication.worker.starts");
    public static readonly Counter<long> WorkerErrors = Meter.CreateCounter<long>("ic.biller.publication.worker.errors");
    public static readonly Counter<long> Claims = Meter.CreateCounter<long>("ic.biller.publication.claims");
    public static readonly Counter<long> Publications = Meter.CreateCounter<long>("ic.biller.publication.results");
    public static readonly Counter<long> ArtifactsUploaded = Meter.CreateCounter<long>("ic.biller.publication.artifacts.uploaded");
    public static readonly Histogram<double> PublicationDuration = Meter.CreateHistogram<double>("ic.biller.publication.duration", "ms");
}
