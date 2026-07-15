using System.Diagnostics;
using Pronto.Agentic.Orchestration.Abstractions;
using Pronto.Agentic.Orchestration.Telemetry;
using Microsoft.Extensions.Logging;

namespace Pronto.Agentic.Orchestration.Execution;

public sealed class ObservableOrchestrationStep<TInput, TOutput>(
    string name,
    string displayName,
    string summary,
    Func<TInput, OrchestrationContext, CancellationToken, ValueTask<TOutput>> execute,
    IOrchestrationEventSink eventSink,
    ILogger? logger = null) : IOrchestrationStep<TInput, TOutput>
{
    private static readonly Action<ILogger, string, string, string?, string, string?, Exception> LogStepFailure =
        LoggerMessage.Define<string, string, string?, string, string?>(
            LogLevel.Error,
            new EventId(9101, nameof(LogStepFailure)),
            "Agent step {AgentId} failed for run {RunId}, biller {BillerId}; error {ErrorCode}, trace {TraceId}");
    private static readonly Action<ILogger, string, string, string?, Exception> LogFailureEventError =
        LoggerMessage.Define<string, string, string?>(
            LogLevel.Error,
            new EventId(9102, nameof(LogFailureEventError)),
            "Publishing failure activity for agent {AgentId}, run {RunId}, biller {BillerId} failed; preserving the original error");
    private static readonly Action<ILogger, string, string, string?, string, Exception> LogActivityEventError =
        LoggerMessage.Define<string, string, string?, string>(
            LogLevel.Error,
            new EventId(9103, nameof(LogActivityEventError)),
            "Publishing {Status} activity for agent {AgentId}, run {RunId}, biller {BillerId} failed");

    public string Name => name;

    public async ValueTask<TOutput> ExecuteAsync(TInput input, OrchestrationContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startedAt = Stopwatch.GetTimestamp();
        using var activity = OrchestrationTelemetry.ActivitySource.StartActivity($"agent:{name}");
        activity?.SetTag("ic.orchestration.agent_id", name);
        activity?.SetTag("ic.orchestration.run_id", context.RunId);
        var sequence = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var tags = new TagList { { "agent", name } };
        OrchestrationTelemetry.StepStarted.Add(1, tags);
        await PublishBestEffortAsync(
            Event(OrchestrationEventStatus.Running, summary, sequence, activity),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var result = await execute(input, context, cancellationToken);
            var durationMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            activity?.SetStatus(ActivityStatusCode.Ok);
            OrchestrationTelemetry.StepCompleted.Add(1, tags);
            OrchestrationTelemetry.StepDuration.Record(durationMs, tags);
            await PublishBestEffortAsync(
                Event(
                    OrchestrationEventStatus.Completed,
                    "Completed",
                    sequence + 1,
                    activity,
                    durationMs: durationMs),
                cancellationToken);
            return result;
        }
        catch (Exception exception)
        {
            var durationMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            var errorCode = exception is OperationCanceledException ? "cancelled" : $"{name}_failed";
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            activity?.AddException(exception);
            OrchestrationTelemetry.StepFailed.Add(1, tags);
            OrchestrationTelemetry.StepDuration.Record(durationMs, tags);
            if (logger is not null)
            {
                LogStepFailure(logger, name, context.RunId, context.BillerId, errorCode, activity?.TraceId.ToString(), exception);
            }
            try
            {
                await eventSink.PublishAsync(Event(OrchestrationEventStatus.Failed, "The agent step failed.", sequence + 1,
                    activity, errorCode, retryable: exception is TimeoutException, durationMs: durationMs), CancellationToken.None);
            }
            catch (Exception sinkException)
            {
                if (logger is not null)
                {
                    LogFailureEventError(logger, name, context.RunId, context.BillerId, sinkException);
                }
            }
            throw;
        }

        async ValueTask PublishBestEffortAsync(
            OrchestrationEvent orchestrationEvent,
            CancellationToken eventCancellationToken)
        {
            try
            {
                await eventSink.PublishAsync(orchestrationEvent, eventCancellationToken);
            }
            catch (Exception exception)
            {
                if (logger is not null)
                {
                    LogActivityEventError(
                        logger,
                        name,
                        context.RunId,
                        context.BillerId,
                        orchestrationEvent.Status.ToString(),
                        exception);
                }
            }
        }

        OrchestrationEvent Event(
            OrchestrationEventStatus status,
            string message,
            long eventSequence,
            Activity? trace,
            string? errorCode = null,
            bool retryable = false,
            double? durationMs = null) =>
            new(Guid.NewGuid().ToString("N"), eventSequence, context.RunId, name, displayName, status, message,
                DateTimeOffset.UtcNow, trace?.TraceId.ToString(), errorCode, retryable, 1, durationMs);
    }
}
