namespace Pronto.Agentic.Orchestration.Abstractions;

public interface IOrchestrationStateStore
{
    ValueTask<OrchestrationCheckpoint?> ReadAsync(
        string partitionKey,
        string runId,
        CancellationToken cancellationToken = default);

    ValueTask<OrchestrationCheckpoint> SaveAsync(
        OrchestrationCheckpoint checkpoint,
        string? expectedETag = null,
        CancellationToken cancellationToken = default);
}

public sealed record OrchestrationCheckpoint(
    string RunId,
    string PartitionKey,
    string Workflow,
    string State,
    int Step,
    string Payload,
    DateTimeOffset UpdatedAt,
    string? ETag = null);
