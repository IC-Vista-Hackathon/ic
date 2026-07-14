using System.Diagnostics;
using Pronto.Agentic.Orchestration.Abstractions;
using Pronto.Agentic.Orchestration.Telemetry;

namespace Pronto.Agentic.Orchestration.Execution;

public sealed class ObservableOrchestrationStep<TInput, TOutput>(
    string name,
    string displayName,
    string summary,
    Func<TInput, OrchestrationContext, CancellationToken, ValueTask<TOutput>> execute,
    IOrchestrationEventSink eventSink) : IOrchestrationStep<TInput, TOutput>
{
    public string Name => name;

    public async ValueTask<TOutput> ExecuteAsync(TInput input, OrchestrationContext context, CancellationToken cancellationToken = default)
    {
        using var activity = OrchestrationTelemetry.ActivitySource.StartActivity($"agent:{name}");
        activity?.SetTag("ic.orchestration.agent_id", name);
        activity?.SetTag("ic.orchestration.run_id", context.RunId);
        var sequence = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await eventSink.PublishAsync(Event(OrchestrationEventStatus.Running, summary, sequence, activity), cancellationToken);
        try
        {
            var result = await execute(input, context, cancellationToken);
            activity?.SetStatus(ActivityStatusCode.Ok);
            await eventSink.PublishAsync(Event(OrchestrationEventStatus.Completed, "Completed", sequence + 1, activity), cancellationToken);
            return result;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            await eventSink.PublishAsync(Event(OrchestrationEventStatus.Failed, "Failed", sequence + 1, activity), CancellationToken.None);
            throw;
        }

        OrchestrationEvent Event(OrchestrationEventStatus status, string message, long eventSequence, Activity? trace) =>
            new(Guid.NewGuid().ToString("N"), eventSequence, context.RunId, name, displayName, status, message,
                DateTimeOffset.UtcNow, trace?.TraceId.ToString());
    }
}
