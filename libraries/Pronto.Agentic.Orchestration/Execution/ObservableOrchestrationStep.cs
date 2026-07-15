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
    private static readonly Action<ILogger, string, string, string, string?, Exception> LogEventPublishError =
        LoggerMessage.Define<string, string, string, string?>(
            LogLevel.Error,
            new EventId(9102, nameof(LogEventPublishError)),
            "Publishing {Status} activity for agent {AgentId}, run {RunId}, biller {BillerId} failed; continuing without failing the step");

    public string Name => name;

    public async ValueTask<TOutput> ExecuteAsync(TInput input, OrchestrationContext context, CancellationToken cancellationToken = default)
    {
        var startedAt = Stopwatch.GetTimestamp();
        using var activity = OrchestrationTelemetry.ActivitySource.StartActivity($"agent:{name}");
        activity?.SetTag("ic.orchestration.agent_id", name);
        activity?.SetTag("ic.orchestration.run_id", context.RunId);
        var sequence = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var tags = new TagList { { "agent", name } };
        OrchestrationTelemetry.StepStarted.Add(1, tags);
        await PublishSafeAsync(Event(OrchestrationEventStatus.Running, summary, sequence, activity), cancellationToken);
        try
        {
            var result = await execute(input, context, cancellationToken);
            var durationMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            activity?.SetStatus(ActivityStatusCode.Ok);
            OrchestrationTelemetry.StepCompleted.Add(1, tags);
            OrchestrationTelemetry.StepDuration.Record(durationMs, tags);
            await PublishSafeAsync(Event(OrchestrationEventStatus.Completed, "Completed", sequence + 1, activity, durationMs: durationMs), cancellationToken);
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
            await PublishSafeAsync(Event(OrchestrationEventStatus.Failed, "The agent step failed.", sequence + 1,
                activity, errorCode, retryable: exception is TimeoutException, durationMs: durationMs), CancellationToken.None);
            throw;
        }

        // Event publication is best-effort observability: a failing sink must never turn a
        // successful step into a failure, discard its result, or mask the original error.
        async ValueTask PublishSafeAsync(OrchestrationEvent activityEvent, CancellationToken token)
        {
            try
            {
                await eventSink.PublishAsync(activityEvent, token);
            }
            catch (Exception sinkException)
            {
                if (logger is not null)
                {
                    LogEventPublishError(logger, activityEvent.Status.ToString(), name, context.RunId, context.BillerId, sinkException);
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
