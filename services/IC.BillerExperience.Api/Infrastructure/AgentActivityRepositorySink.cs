using IC.Agentic.Orchestration.Abstractions;
using IC.BillerExperience.Api.Infrastructure.Persistence;
using IC.BillerExperience.Contracts.V1.Onboarding;
using System.Collections.Concurrent;

namespace IC.BillerExperience.Api.Infrastructure;

public sealed partial class AgentActivityRepositorySink(
    IBillerExperienceRepository repository,
    string billerId,
    string runId,
    ILogger logger) : IOrchestrationEventSink
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> RunLocks = new(StringComparer.Ordinal);

    public async ValueTask PublishAsync(OrchestrationEvent activity, CancellationToken cancellationToken = default)
    {
        var runLock = RunLocks.GetOrAdd($"{billerId}:{runId}", static _ => new SemaphoreSlim(1, 1));
        await runLock.WaitAsync(cancellationToken);
        try
        {
            _ = await repository.GetRunAsync(billerId, runId, cancellationToken)
                ?? throw new KeyNotFoundException($"Onboarding run '{runId}' was not found.");
            var existing = await repository.GetAgentActivityAsync(billerId, runId, cancellationToken);
            var nextSequence = existing.Count == 0 ? 1 : existing.Max(item => item.Sequence) + 1;
            var mapped = new AgentActivityEvent(activity.EventId, activity.Sequence, activity.RunId,
                activity.AgentId, activity.DisplayName, MapStatus(activity.Status),
                activity.Summary, activity.OccurredAt, activity.TraceId, activity.ErrorCode,
                activity.Retryable, activity.Attempt, activity.DurationMs) with { Sequence = nextSequence };
            await repository.AppendAgentActivityAsync(billerId, runId, mapped, cancellationToken);
            LogActivity(logger, billerId, activity.AgentId, activity.Status.ToString(), activity.EventId);
        }
        catch (Exception exception)
        {
            LogActivityError(logger, billerId, activity.AgentId, activity.EventId, exception);
            throw;
        }
        finally
        {
            runLock.Release();
        }
    }

    private static AgentActivityStatus MapStatus(OrchestrationEventStatus status) => status switch
    {
        OrchestrationEventStatus.Discovered => AgentActivityStatus.Discovered,
        OrchestrationEventStatus.Queued => AgentActivityStatus.Queued,
        OrchestrationEventStatus.Running => AgentActivityStatus.Running,
        OrchestrationEventStatus.Completed => AgentActivityStatus.Completed,
        OrchestrationEventStatus.NeedsInput => AgentActivityStatus.NeedsInput,
        OrchestrationEventStatus.Failed => AgentActivityStatus.Failed,
        OrchestrationEventStatus.Retrying => AgentActivityStatus.Retrying,
        OrchestrationEventStatus.Degraded => AgentActivityStatus.Degraded,
        _ => AgentActivityStatus.Failed
    };

    [LoggerMessage(2400, LogLevel.Information, "Agent activity {EventId} for biller {BillerId}: {AgentId} is {Status}")]
    private static partial void LogActivity(ILogger logger, string billerId, string agentId, string status, string eventId);

    [LoggerMessage(2499, LogLevel.Error, "Persisting agent activity {EventId} for biller {BillerId}, agent {AgentId} failed")]
    private static partial void LogActivityError(ILogger logger, string billerId, string agentId, string eventId, Exception exception);
}
