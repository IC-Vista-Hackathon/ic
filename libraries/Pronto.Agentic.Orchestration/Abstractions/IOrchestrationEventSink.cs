namespace Pronto.Agentic.Orchestration.Abstractions;

public interface IOrchestrationEventSink
{
    ValueTask PublishAsync(OrchestrationEvent activity, CancellationToken cancellationToken = default);
}

public sealed class NullOrchestrationEventSink : IOrchestrationEventSink
{
    public ValueTask PublishAsync(OrchestrationEvent activity, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;
}
