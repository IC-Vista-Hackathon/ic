using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace IC.BillerExperience.Worker;

public sealed partial class PublicationWorker(ILogger<PublicationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var activity = PublicationTelemetry.Source.StartActivity("publication.worker.run");
        var traceId = activity?.TraceId.ToString();
        try
        {
            LogWorkerReady(logger, traceId);
            PublicationTelemetry.WorkerStarts.Add(1);
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            LogWorkerStopped(logger, traceId);
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            PublicationTelemetry.WorkerErrors.Add(1);
            LogWorkerError(logger, traceId, exception);
            throw;
        }
    }

    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "IC Biller Experience publication worker is ready; trace {TraceId}")]
    private static partial void LogWorkerReady(ILogger logger, string? traceId);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "IC Biller Experience publication worker stopped; trace {TraceId}")]
    private static partial void LogWorkerStopped(ILogger logger, string? traceId);

    [LoggerMessage(EventId = 1900, Level = LogLevel.Error, Message = "IC Biller Experience publication worker failed; trace {TraceId}")]
    private static partial void LogWorkerError(ILogger logger, string? traceId, Exception exception);
}

public static class PublicationTelemetry
{
    public const string SourceName = "IC.BillerExperience.Publication";
    public const string MeterName = "IC.BillerExperience.Publication";
    public static readonly ActivitySource Source = new(SourceName);
    public static readonly Meter Meter = new(MeterName);
    public static readonly Counter<long> WorkerStarts = Meter.CreateCounter<long>("ic.biller.publication.worker.starts");
    public static readonly Counter<long> WorkerErrors = Meter.CreateCounter<long>("ic.biller.publication.worker.errors");
}
