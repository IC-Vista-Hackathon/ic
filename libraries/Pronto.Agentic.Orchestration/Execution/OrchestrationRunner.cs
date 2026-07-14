using System.Diagnostics;
using Pronto.Agentic.Orchestration.Abstractions;
using Pronto.Agentic.Orchestration.Telemetry;

namespace Pronto.Agentic.Orchestration.Execution;

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

        var startedAt = Stopwatch.GetTimestamp();
        var tags = new TagList { { "workflow", workflow.Name } };
        OrchestrationTelemetry.WorkflowStarted.Add(1, tags);

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
            OrchestrationTelemetry.WorkflowCompleted.Add(1, tags);
            return result;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            OrchestrationTelemetry.WorkflowFailed.Add(1, tags);
            throw;
        }
        finally
        {
            OrchestrationTelemetry.WorkflowDuration.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, tags);
        }
    }
}
