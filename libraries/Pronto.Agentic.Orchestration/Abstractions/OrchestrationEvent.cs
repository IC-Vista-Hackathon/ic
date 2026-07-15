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
    string? TraceId = null,
    string? ErrorCode = null,
    bool Retryable = false,
    int Attempt = 1,
    double? DurationMs = null);

public enum OrchestrationEventStatus
{
    Discovered,
    Queued,
    Running,
    Completed,
    NeedsInput,
    Failed,
    Retrying,
    Degraded
}
