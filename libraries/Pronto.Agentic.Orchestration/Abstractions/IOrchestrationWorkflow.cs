namespace Pronto.Agentic.Orchestration.Abstractions;

public interface IOrchestrationWorkflow<in TInput, TOutput>
{
    string Name { get; }

    ValueTask<TOutput> ExecuteAsync(
        TInput input,
        OrchestrationContext context,
        CancellationToken cancellationToken = default);
}
