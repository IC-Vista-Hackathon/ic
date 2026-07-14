namespace Pronto.Agentic.Orchestration.Abstractions;

public interface IOrchestrationRunner
{
    ValueTask<TOutput> RunAsync<TInput, TOutput>(
        IOrchestrationWorkflow<TInput, TOutput> workflow,
        TInput input,
        OrchestrationContext context,
        CancellationToken cancellationToken = default);
}
