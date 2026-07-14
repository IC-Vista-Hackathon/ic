namespace Pronto.Agentic.Orchestration.Abstractions;

public sealed record OrchestrationEvent(
    string EventId,
    long Sequence,
    string RunId,
    string AgentId,
    string DisplayName,
    OrchestrationEventStatus Status,
    string Summary,
    DateTimeOffset OccurredAt,
    string? TraceId = null);

public enum OrchestrationEventStatus
{
    Queued,
    Running,
    Completed,
    NeedsInput,
    Failed
}
