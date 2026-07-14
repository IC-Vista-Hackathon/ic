namespace IC.BillerExperience.Worker;

public sealed partial class PublicationWorker(ILogger<PublicationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogWorkerReady(logger);

        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }

    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "IC Biller Experience publication worker is ready")]
    private static partial void LogWorkerReady(ILogger logger);
}
