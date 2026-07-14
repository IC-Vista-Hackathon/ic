using System.Diagnostics;

namespace IC.Agentic.Orchestration.Telemetry;

public static class OrchestrationTelemetry
{
    public const string ActivitySourceName = "IC.Agentic.Orchestration";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}
