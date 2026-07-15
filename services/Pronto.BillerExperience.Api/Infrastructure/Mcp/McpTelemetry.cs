using System.Diagnostics;
using System.Diagnostics.Metrics;
using Pronto.BillerExperience.Api.Infrastructure;

namespace Pronto.BillerExperience.Api.Infrastructure.Mcp;

/// <summary>Privacy-safe OpenTelemetry events and metrics for the MCP service router.</summary>
internal static class McpTelemetry
{
    internal const string ToolInvokedEvent = "mcp.tool_invoked";
    internal const string ToolCompletedEvent = "mcp.tool_completed";
    internal const string ToolFailedEvent = "mcp.tool_failed";

    private static readonly Counter<long> Invoked = BillerExperienceTelemetry.Meter.CreateCounter<long>("ic.mcp.tool.invoked");
    private static readonly Counter<long> Completed = BillerExperienceTelemetry.Meter.CreateCounter<long>("ic.mcp.tool.completed");
    private static readonly Counter<long> Failed = BillerExperienceTelemetry.Meter.CreateCounter<long>("ic.mcp.tool.failed");
    private static readonly Histogram<double> Duration = BillerExperienceTelemetry.Meter.CreateHistogram<double>("ic.mcp.tool.duration", "ms");

    internal static (string Category, int StatusCode) Categorize(Exception exception) => exception switch
    {
        UnauthorizedAccessException => ("unauthorized", 403),
        KeyNotFoundException => ("not_found", 404),
        ArgumentException => ("invalid_argument", 400),
        InvalidOperationException => ("invalid_state", 409),
        _ => ("internal_error", 500),
    };

    internal static Activity? StartToolActivity(string toolName, AgentContextCapability capability)
    {
        var activity = BillerExperienceTelemetry.Source.StartActivity("mcp.tool.invoke", ActivityKind.Internal);
        if (activity is null) return null;
        activity.SetTag("tool_name", toolName);
        activity.SetTag("biller_id", capability.BillerId);
        activity.SetTag("agent_id", capability.AgentId);
        activity.SetTag("run_id", capability.RunId);
        activity.SetTag("write_capable", capability.CanWrite);
        activity.SetTag("payer_bound", capability.PayerId is not null);
        return activity;
    }

    internal static void RecordInvoked(string toolName, AgentContextCapability capability, Activity? activity)
    {
        var writeCapable = capability.CanWrite;
        var payerBound = capability.PayerId is not null;
        Invoked.Add(1,
            new("tool", toolName),
            new("write_capable", writeCapable),
            new("payer_bound", payerBound));
        (activity ?? Activity.Current)?.AddEvent(new ActivityEvent(ToolInvokedEvent, tags: new ActivityTagsCollection
        {
            ["tool_name"] = toolName,
            ["biller_id"] = capability.BillerId,
            ["agent_id"] = capability.AgentId,
            ["run_id"] = capability.RunId,
            ["write_capable"] = writeCapable,
            ["payer_bound"] = payerBound,
            ["trace_id"] = TraceId(activity),
        }));
    }

    internal static void RecordCompleted(string toolName, AgentContextCapability capability, double elapsedMs, Activity? activity)
    {
        var writeCapable = capability.CanWrite;
        var payerBound = capability.PayerId is not null;
        Completed.Add(1,
            new("tool", toolName),
            new("write_capable", writeCapable),
            new("payer_bound", payerBound));
        Duration.Record(elapsedMs, new("tool", toolName), new("outcome", "ok"));
        activity?.SetStatus(ActivityStatusCode.Ok);
        (activity ?? Activity.Current)?.AddEvent(new ActivityEvent(ToolCompletedEvent, tags: new ActivityTagsCollection
        {
            ["tool_name"] = toolName,
            ["biller_id"] = capability.BillerId,
            ["agent_id"] = capability.AgentId,
            ["run_id"] = capability.RunId,
            ["write_capable"] = writeCapable,
            ["payer_bound"] = payerBound,
            ["outcome"] = "ok",
            ["duration_ms"] = elapsedMs,
            ["trace_id"] = TraceId(activity),
        }));
    }

    internal static void RecordFailed(
        string toolName, AgentContextCapability capability, double elapsedMs, string category, int statusCode, Activity? activity)
    {
        var writeCapable = capability.CanWrite;
        var payerBound = capability.PayerId is not null;
        Failed.Add(1,
            new("tool", toolName),
            new("failure_category", category),
            new("write_capable", writeCapable),
            new("payer_bound", payerBound));
        Duration.Record(elapsedMs, new("tool", toolName), new("outcome", "error"));
        activity?.SetStatus(ActivityStatusCode.Error, category);
        (activity ?? Activity.Current)?.AddEvent(new ActivityEvent(ToolFailedEvent, tags: new ActivityTagsCollection
        {
            ["tool_name"] = toolName,
            ["biller_id"] = capability.BillerId,
            ["agent_id"] = capability.AgentId,
            ["run_id"] = capability.RunId,
            ["write_capable"] = writeCapable,
            ["payer_bound"] = payerBound,
            ["outcome"] = "error",
            ["failure_category"] = category,
            ["status_code"] = statusCode,
            ["duration_ms"] = elapsedMs,
            ["trace_id"] = TraceId(activity),
        }));
    }

    internal static void RecordDenied(string toolName)
    {
        Failed.Add(1,
            new("tool", toolName),
            new("failure_category", "unauthorized"),
            new("write_capable", false),
            new("payer_bound", false));
        Activity.Current?.AddEvent(new ActivityEvent(ToolFailedEvent, tags: new ActivityTagsCollection
        {
            ["tool_name"] = toolName,
            ["outcome"] = "error",
            ["failure_category"] = "unauthorized",
            ["status_code"] = 403,
            ["trace_id"] = Activity.Current?.TraceId.ToString(),
        }));
    }

    private static string? TraceId(Activity? activity) =>
        (activity ?? Activity.Current)?.TraceId.ToString();
}
