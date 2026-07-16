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
    ILogger? logger = null,
    Func<TOutput, (OrchestrationEventStatus Status, string Summary, string? ErrorCode)>? completion = null,
    Func<TOutput, IReadOnlyList<string>>? warnings = null)
    : IOrchestrationStep<TInput, TOutput>
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
            var outcome = completion?.Invoke(result) ?? (OrchestrationEventStatus.Completed, "Completed", null);
            // Carry the resolved outcome (Completed/Degraded/Skipped/NeedsInput) as a metric dimension and
            // trace tag so degraded/skipped steps stay countable and trace-filterable — a bare
            // StepCompleted with only an agent tag hides them behind the same counter as clean runs.
            var status = outcome.Item1.ToString();
            var outcomeTags = new TagList { { "agent", name }, { "status", status } };
            activity?.SetTag("ic.orchestration.status", status);
            if (outcome.Item3 is not null)
            {
                activity?.SetTag("ic.orchestration.error_code", outcome.Item3);
            }
            activity?.SetStatus(ActivityStatusCode.Ok);
            OrchestrationTelemetry.StepCompleted.Add(1, outcomeTags);
            OrchestrationTelemetry.StepDuration.Record(durationMs, outcomeTags);
            await PublishSafeAsync(Event(outcome.Item1, outcome.Item2, sequence + 1, activity,
                errorCode: outcome.Item3, durationMs: durationMs, warnings: warnings?.Invoke(result)), cancellationToken);
            return result;
        }
        catch (Exception exception)
        {
            var durationMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            var errorCode = exception is OperationCanceledException ? "cancelled" : $"{name}_failed";
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            activity?.SetTag("ic.orchestration.status", OrchestrationEventStatus.Failed.ToString());
            activity?.SetTag("ic.orchestration.error_code", errorCode);
            activity?.AddException(exception);
            var failedTags = new TagList { { "agent", name }, { "status", OrchestrationEventStatus.Failed.ToString() } };
            OrchestrationTelemetry.StepFailed.Add(1, failedTags);
            OrchestrationTelemetry.StepDuration.Record(durationMs, failedTags);
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
            double? durationMs = null,
            IReadOnlyList<string>? warnings = null) =>
            new(Guid.NewGuid().ToString("N"), eventSequence, context.RunId, name, displayName, status, message,
                DateTimeOffset.UtcNow, trace?.TraceId.ToString(), errorCode, retryable, 1, durationMs,
                warnings is { Count: > 0 } ? warnings : null);
    }
}
