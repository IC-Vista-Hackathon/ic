using System.Diagnostics;
using IC.Agentic.Orchestration.Abstractions;
using IC.Agentic.Orchestration.Telemetry;

namespace IC.Agentic.Orchestration.Execution;

public sealed class OrchestrationRunner : IOrchestrationRunner
{
    public async ValueTask<TOutput> RunAsync<TInput, TOutput>(
        IOrchestrationWorkflow<TInput, TOutput> workflow,
        TInput input,
        OrchestrationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(context);

        using var activity = OrchestrationTelemetry.ActivitySource.StartActivity(
            $"workflow:{workflow.Name}",
            ActivityKind.Internal);

        activity?.SetTag("ic.orchestration.workflow", workflow.Name);
        activity?.SetTag("ic.orchestration.run_id", context.RunId);
        activity?.SetTag("ic.correlation_id", context.CorrelationId);
        activity?.SetTag("ic.biller_id", context.BillerId);
        activity?.SetTag("ic.session_id", context.SessionId);

        try
        {
            var result = await workflow.ExecuteAsync(input, context, cancellationToken);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return result;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            throw;
        }
    }
}
