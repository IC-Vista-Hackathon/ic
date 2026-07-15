using System.ComponentModel;
using IC.BillerExperience.Api.Application;
using IC.BillerExperience.Contracts.V1.AgentContext;
using ModelContextProtocol.Server;

namespace IC.BillerExperience.Api.Infrastructure.Mcp;

[McpServerToolType]
public sealed partial class AgentContextMcpTools(
    AgentContextService contextService,
    AgentContextCapabilityService capabilities,
    ILogger<AgentContextMcpTools> logger)
{
    [McpServerTool(Name = "get_goal_context", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Read the current biller-scoped goal, accepted artifacts, observations, corrections, and unresolved questions. The capability token determines tenant and run scope.")]
    public async ValueTask<AgentContextSnapshot> GetGoalContextAsync(
        [Description("Short-lived biller/run/agent capability issued by IC orchestration.")] string capabilityToken,
        CancellationToken cancellationToken)
    {
        try
        {
            var capability = capabilities.Validate(capabilityToken, writeRequired: false);
            return await contextService.GetAsync(capability.BillerId, capability.RunId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogGetFailed(logger, System.Diagnostics.Activity.Current?.TraceId.ToString(), exception);
            throw;
        }
    }

    [McpServerTool(Name = "append_context", Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Append one provenance-bearing observation, artifact, correction, or unresolved question to shared biller context. Never include secrets, payment instruments, personal data, or private chain-of-thought.")]
    public async ValueTask<AgentContextSnapshot> AppendContextAsync(
        [Description("Short-lived writable biller/run/agent capability issued by IC orchestration.")] string capabilityToken,
        [Description("Context version returned by get_goal_context; prevents lost updates.")] long expectedVersion,
        [Description("One of observation, candidate_artifact, accepted_artifact, correction, or unresolved_question.")] AgentContextEntryKind kind,
        [Description("Bounded business scope for the entry, such as research or accessibility.")] string scope,
        [Description("Concise conclusion and evidence; never private chain-of-thought.")] string content,
        [Description("Absolute HTTPS citations supporting external claims.")] string[] sources,
        [Description("True when the content is based on public web or another external source.")] bool external,
        CancellationToken cancellationToken)
    {
        try
        {
            var capability = capabilities.Validate(capabilityToken, writeRequired: true);
            var sourceUris = sources.Select(source =>
            {
                if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                    throw new ArgumentException("Every MCP context source must be an absolute HTTPS URI.", nameof(sources));
                return uri;
            }).ToArray();
            return await contextService.AppendAsync(
                capability.BillerId,
                capability.RunId,
                new AppendAgentContextRequest(
                    expectedVersion,
                    kind,
                    capability.AgentId,
                    scope,
                    content,
                    sourceUris,
                    external),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogAppendFailed(logger, scope, System.Diagnostics.Activity.Current?.TraceId.ToString(), exception);
            throw;
        }
    }

    [LoggerMessage(2791, LogLevel.Error, "MCP get_goal_context failed; trace {TraceId}")]
    private static partial void LogGetFailed(ILogger logger, string? traceId, Exception exception);

    [LoggerMessage(2792, LogLevel.Error, "MCP append_context failed for scope {Scope}; trace {TraceId}")]
    private static partial void LogAppendFailed(ILogger logger, string scope, string? traceId, Exception exception);
}
