using Pronto.Agentic.Orchestration.Abstractions;
using Pronto.BillerExperience.Api.Infrastructure.Persistence;
using Pronto.BillerExperience.Contracts.V1.Onboarding;

namespace Pronto.BillerExperience.Api.Infrastructure;

public sealed partial class AgentActivityRepositorySink(
    IBillerExperienceRepository repository,
    string billerId,
    string runId,
    ILogger logger) : IOrchestrationEventSink
{
    public async ValueTask PublishAsync(OrchestrationEvent activity, CancellationToken cancellationToken = default)
    {
        try
        {
            var run = await repository.GetRunAsync(billerId, runId, cancellationToken)
                ?? throw new KeyNotFoundException($"Onboarding run '{runId}' was not found.");
            var mapped = new AgentActivityEvent(activity.EventId, activity.Sequence, activity.RunId,
                activity.AgentId, activity.DisplayName, (AgentActivityStatus)(int)activity.Status,
                activity.Summary, activity.OccurredAt, activity.TraceId);
            var events = (run.AgentActivity ?? []).Append(mapped).TakeLast(100).ToArray();
            await repository.SaveRunAsync(run with { AgentActivity = events, UpdatedAt = DateTimeOffset.UtcNow }, run.ETag, cancellationToken);
            LogActivity(logger, billerId, activity.AgentId, activity.Status.ToString(), activity.EventId);
        }
        catch (Exception exception)
        {
            LogActivityError(logger, billerId, activity.AgentId, activity.EventId, exception);
            throw;
        }
    }

    [LoggerMessage(2400, LogLevel.Information, "Agent activity {EventId} for biller {BillerId}: {AgentId} is {Status}")]
    private static partial void LogActivity(ILogger logger, string billerId, string agentId, string status, string eventId);

    [LoggerMessage(2499, LogLevel.Error, "Persisting agent activity {EventId} for biller {BillerId}, agent {AgentId} failed")]
    private static partial void LogActivityError(ILogger logger, string billerId, string agentId, string eventId, Exception exception);
}
