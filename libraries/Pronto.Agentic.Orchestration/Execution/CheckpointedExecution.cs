using System.Text.Json;
using Pronto.Agentic.Orchestration.Abstractions;

namespace Pronto.Agentic.Orchestration.Execution;

public static class CheckpointedExecution
{
    public static async ValueTask<TOutput> ExecuteAsync<TOutput>(
        IOrchestrationStateStore store,
        string partitionKey,
        string runId,
        string workflow,
        int step,
        Func<CancellationToken, ValueTask<TOutput>> operation,
        JsonSerializerOptions? jsonOptions = null,
        CancellationToken cancellationToken = default)
    {
        var current = await store.ReadAsync(partitionKey, runId, cancellationToken);
        if (current is { State: "completed" } && current.Step >= step)
            return JsonSerializer.Deserialize<TOutput>(current.Payload, jsonOptions)
                ?? throw new InvalidOperationException("Completed orchestration checkpoint had no output.");

        var running = await store.SaveAsync(new(runId, partitionKey, workflow, "running", step,
            current?.Payload ?? "null", DateTimeOffset.UtcNow), current?.ETag, cancellationToken);
        var output = await operation(cancellationToken);
        var completed = running with
        {
            State = "completed",
            Payload = JsonSerializer.Serialize(output, jsonOptions),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await store.SaveAsync(completed, running.ETag, cancellationToken);
        return output;
    }
}
