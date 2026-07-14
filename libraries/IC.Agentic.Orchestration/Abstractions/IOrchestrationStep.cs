namespace IC.Agentic.Orchestration.Abstractions;

public interface IOrchestrationStep<in TInput, TOutput>
{
    string Name { get; }

    ValueTask<TOutput> ExecuteAsync(
        TInput input,
        OrchestrationContext context,
        CancellationToken cancellationToken = default);
}
