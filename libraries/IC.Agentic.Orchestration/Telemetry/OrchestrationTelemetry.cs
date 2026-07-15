using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace IC.Agentic.Orchestration.Telemetry;

public static class OrchestrationTelemetry
{
    public const string ActivitySourceName = "IC.Agentic.Orchestration";
    public const string MeterName = "IC.Agentic.Orchestration";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);
    public static readonly Counter<long> WorkflowStarted = Meter.CreateCounter<long>("ic.orchestration.workflow.started");
    public static readonly Counter<long> WorkflowCompleted = Meter.CreateCounter<long>("ic.orchestration.workflow.completed");
    public static readonly Counter<long> WorkflowFailed = Meter.CreateCounter<long>("ic.orchestration.workflow.failed");
    public static readonly Histogram<double> WorkflowDuration = Meter.CreateHistogram<double>("ic.orchestration.workflow.duration", "ms");
    public static readonly Counter<long> StepStarted = Meter.CreateCounter<long>("ic.orchestration.step.started");
    public static readonly Counter<long> StepCompleted = Meter.CreateCounter<long>("ic.orchestration.step.completed");
    public static readonly Counter<long> StepFailed = Meter.CreateCounter<long>("ic.orchestration.step.failed");
    public static readonly Histogram<double> StepDuration = Meter.CreateHistogram<double>("ic.orchestration.step.duration", "ms");
}
