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
        // Each step of a run owns its own checkpoint document. A single checkpoint per run
        // could only hold the latest step's payload, so resuming an earlier step would return
        // the wrong step's output (deserialized into the wrong type). Scoping the persistence
        // key by step keeps every step's output independently resumable.
        var checkpointId = CheckpointId(runId, step);
        var current = await store.ReadAsync(partitionKey, checkpointId, cancellationToken);
        if (current is { State: "completed" })
            return JsonSerializer.Deserialize<TOutput>(current.Payload, jsonOptions)!;

        var running = await store.SaveAsync(new(checkpointId, partitionKey, workflow, "running", step,
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

    private static string CheckpointId(string runId, int step) => $"{runId}#step={step}";
}
