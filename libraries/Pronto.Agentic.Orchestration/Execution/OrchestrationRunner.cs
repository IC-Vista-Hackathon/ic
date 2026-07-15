using System.Diagnostics;
using Pronto.Agentic.Orchestration.Abstractions;
using Pronto.Agentic.Orchestration.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Pronto.Agentic.Orchestration.Execution;

public sealed class OrchestrationRunner(ILogger<OrchestrationRunner>? logger = null) : IOrchestrationRunner
{
    private static readonly Action<ILogger, string, string, string?, string?, Exception> LogWorkflowFailure =
        LoggerMessage.Define<string, string, string?, string?>(
            LogLevel.Error,
            new EventId(9001, nameof(LogWorkflowFailure)),
            "Orchestration workflow {Workflow} failed for run {RunId}, biller {BillerId}; trace {TraceId}");
    private readonly ILogger<OrchestrationRunner> logger = logger ?? NullLogger<OrchestrationRunner>.Instance;

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
            activity?.AddException(exception);
            OrchestrationTelemetry.WorkflowFailed.Add(1, tags);
            LogWorkflowFailure(logger, workflow.Name, context.RunId, context.BillerId, activity?.TraceId.ToString(), exception);
            throw;
        }
        finally
        {
            OrchestrationTelemetry.WorkflowDuration.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, tags);
        }
    }
}
