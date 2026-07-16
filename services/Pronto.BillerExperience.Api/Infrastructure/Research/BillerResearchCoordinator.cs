using System.Diagnostics;
using Pronto.Agentic.Orchestration.Abstractions;
using Pronto.BillerExperience.Api.Configuration;
using Pronto.BillerExperience.Contracts.V1.AgentContext;
using Pronto.BillerExperience.Contracts.V1.Research;
using Microsoft.Extensions.Options;
using Pronto.Agentic.Orchestration.Execution;

namespace Pronto.BillerExperience.Api.Infrastructure.Research;

public sealed partial class BillerResearchCoordinator(
    IResearchAgentCatalog catalog,
    IResearchAgentDispatcher dispatcher,
    IOptions<BillerExperienceOptions> options,
    ILogger<BillerResearchCoordinator> logger,
    IFoundryResearchConsolidator? consolidator = null,
    IAgentContextCapabilityIssuer? capabilityIssuer = null,
    IAgentContextMcpGateway? contextGateway = null) : IBillerResearchCoordinator
{
    private readonly ResearchOptions _options = options.Value.Research;
    private readonly McpOptions _optionsForMcp = options.Value.Mcp;

    public async Task<BillerResearchResponse> ResearchAsync(
        BillerResearchRequest request,
        ResearchExecutionContext? executionContext = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = BillerExperienceTelemetry.Source.StartActivity("research.coordinate");
        var startedAt = Stopwatch.GetTimestamp();
        ResearchTelemetry.Requests.Add(1);
        IReadOnlyList<ResearchAgentDescriptor> discovered;
        try
        {
            discovered = await catalog.ListAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogCatalogFailure(logger, exception, Activity.Current?.TraceId.ToString());
            return Record(activity, startedAt, Failed("research.catalog_failed", retryable: true));
        }
        catch (OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "research.cancelled");
            throw;
        }

        foreach (var agent in discovered)
        {
            var eligible = IsEligible(agent, _options.AllowedAgentIds);
            var eligibilitySummary = eligible
                ? $"Discovered approved {agent.Provider} agent with capability {_options.RequiredCapability}."
                : DescribeIneligibility(agent, _options.AllowedAgentIds);
            await PublishActivityAsync(
                executionContext,
                agent,
                OrchestrationEventStatus.Discovered,
                eligibilitySummary,
                cancellationToken: cancellationToken);
            if (!eligible)
            {
                await PublishActivityAsync(
                    executionContext,
                    agent,
                    OrchestrationEventStatus.Skipped,
                    eligibilitySummary,
                    "research.agent_ineligible",
                    cancellationToken: cancellationToken);
            }
        }

        var allowlist = _options.AllowedAgentIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var agents = discovered
            .Where(agent => agent.Approved)
            .Where(agent => agent.Enabled)
            .Where(agent => allowlist.Count == 0 || allowlist.Contains(agent.Id))
            .Where(agent => agent.Capabilities.Any(capability =>
                capability.Equals(_options.RequiredCapability, StringComparison.OrdinalIgnoreCase)))
            .Take(Math.Max(1, _options.MaxAgentCount))
            .ToArray();

        if (agents.Length == 0)
        {
            LogNoEligibleAgents(logger, discovered.Count, Activity.Current?.TraceId.ToString());
            return Record(activity, startedAt, Failed("research.no_eligible_agents", retryable: false));
        }

        activity?.SetTag("research.agent.count", agents.Length);
        var fanOut = await BoundedFanOut.ExecuteAsync<ResearchAgentDescriptor, AgentResult>(
            agents,
            Math.Max(1, _options.MaxParallelAgents),
            (agent, _, token) => new ValueTask<AgentResult>(DispatchAsync(agent, request, executionContext, token)),
            cancellationToken: cancellationToken);
        var results = fanOut.Select(item => item.Output ?? new AgentResult(null, "research.agent_failed", true)).ToArray();
        var successful = results.Where(result => result.Response is not null).Select(result => result.Response!).ToArray();
        var warnings = results.Where(result => result.ErrorCode is not null)
            .Select(result => result.ErrorCode!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (successful.Length == 0)
        {
            var errorCode = warnings.Count == 1 ? warnings[0] : "research.all_agents_failed";
            var retryable = results.Any(result => result.Retryable);
            return Record(activity, startedAt,
                new BillerResearchResponse(ResearchOutcome.Failed, [], [], warnings, errorCode, retryable));
        }

        if (successful.All(response => response.Outcome == ResearchOutcome.Skipped))
        {
            var skipWarnings = successful.SelectMany(response => response.Warnings)
                .Concat(warnings)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var skipCode = successful.Select(response => response.ErrorCode).FirstOrDefault(code => code is not null)
                ?? "research.skipped";
            activity?.SetTag("research.agent.skipped", successful.Length);
            return Record(activity, startedAt,
                new BillerResearchResponse(ResearchOutcome.Skipped, [], [], skipWarnings, skipCode));
        }

        var facts = successful.SelectMany(response => response.Facts)
            .GroupBy(fact => (fact.Name, fact.Value, fact.SourceUrl))
            .Select(group => group.OrderByDescending(fact => fact.Confidence).First())
            .ToArray();
        var sources = successful.SelectMany(response => response.Sources)
            .GroupBy(source => source.Url)
            .Select(group => group.OrderByDescending(source => source.RetrievedAt).First())
            .ToArray();
        warnings.AddRange(successful.SelectMany(response => response.Warnings));
        warnings = warnings.Distinct(StringComparer.Ordinal).ToList();

        var merged = new BillerResearchResponse(ResearchOutcome.Completed, facts, sources, warnings);
        if (consolidator is not null && successful.Length > 1)
        {
            try
            {
                var consolidated = await consolidator.ConsolidateAsync(request, successful, executionContext, cancellationToken);
                if (consolidated.Outcome != ResearchOutcome.Failed)
                {
                    activity?.SetTag("research.consolidated", true);
                    activity?.SetTag("research.agent.succeeded", successful.Length);
                    var consolidatedWarnings = consolidated.Warnings.Concat(warnings)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();
                    return Record(activity, startedAt, FinalizeOutcome(consolidated with { Warnings = consolidatedWarnings }));
                }

                var code = consolidated.ErrorCode ?? "research.consolidation_failed";
                LogConsolidationFailure(logger, code, Activity.Current?.TraceId.ToString());
                warnings.Add(code);
            }
            catch (Exception exception)
            {
                LogConsolidationException(logger, Activity.Current?.TraceId.ToString(), exception);
                warnings.Add("research.consolidation_failed");
            }
        }

        activity?.SetTag("research.agent.succeeded", successful.Length);
        return Record(activity, startedAt, FinalizeOutcome(merged with { Warnings = warnings }));
    }

    // Single exit stamp for the coordinator span + metrics so outcome (including Degraded) is
    // countable and trace-filterable. Degraded is a successful-but-caveated result, so the span
    // status stays Ok (filter on the research.outcome tag); only genuine failures set Error.
    private static BillerResearchResponse Record(Activity? activity, long startedAt, BillerResearchResponse response)
    {
        var durationMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        var outcome = response.Outcome.ToString();
        ResearchTelemetry.CoordinationDuration.Record(durationMs, new KeyValuePair<string, object?>("outcome", outcome));
        ResearchTelemetry.Coordinations.Add(1, new("outcome", outcome), new("error_type", response.ErrorCode ?? "none"));
        if (response.Outcome == ResearchOutcome.Failed)
        {
            ResearchTelemetry.Failures.Add(1, new KeyValuePair<string, object?>("error_type", response.ErrorCode ?? "research.failed"));
        }
        activity?.SetTag("research.outcome", outcome);
        if (response.ErrorCode is not null)
        {
            activity?.SetTag("research.error_code", response.ErrorCode);
        }
        activity?.SetStatus(
            response.Outcome == ResearchOutcome.Failed ? ActivityStatusCode.Error : ActivityStatusCode.Ok,
            response.ErrorCode);
        return response;
    }

    // A run is degraded only when an operational warning is present. Operational warnings are the
    // orchestration's own "research.*" codes (an agent failed, timed out, was skipped, consolidation
    // or MCP context failed). Free-form data-quality caveats an agent writes into its own result are
    // advisory and are surfaced without flipping the badge, so a clean run reports Completed.
    private BillerResearchResponse FinalizeOutcome(BillerResearchResponse response)
    {
        var warnings = response.Warnings.Distinct(StringComparer.Ordinal).ToArray();
        var operational = warnings.Where(IsOperationalWarning).ToArray();
        var advisory = warnings.Where(warning => !IsOperationalWarning(warning)).ToArray();
        if (advisory.Length > 0)
        {
            LogAdvisoryWarnings(logger, string.Join(", ", advisory), Activity.Current?.TraceId.ToString());
        }

        if (operational.Length == 0)
        {
            return response with
            {
                Outcome = response.Outcome == ResearchOutcome.Failed ? ResearchOutcome.Failed : ResearchOutcome.Completed,
                Warnings = warnings
            };
        }

        LogDegraded(logger, string.Join(", ", operational), Activity.Current?.TraceId.ToString());
        return response with
        {
            Outcome = ResearchOutcome.Degraded,
            Warnings = warnings,
            ErrorCode = response.ErrorCode ?? operational[0]
        };
    }

    private static bool IsOperationalWarning(string code) =>
        code.StartsWith("research.", StringComparison.Ordinal);

    private async Task<AgentResult> DispatchAsync(
        ResearchAgentDescriptor agent,
        BillerResearchRequest request,
        ResearchExecutionContext? executionContext,
        CancellationToken cancellationToken)
    {
        await PublishActivityAsync(
                executionContext,
                agent,
                OrchestrationEventStatus.Running,
                $"Invoking {agent.Provider} agent for cited biller research.",
                cancellationToken: cancellationToken);
            var startedAt = Stopwatch.GetTimestamp();

            void RecordDispatch(string outcome, string? errorType) =>
                ResearchTelemetry.AgentDispatches.Add(1,
                    new("agent", agent.Id),
                    new("provider", agent.Provider),
                    new("outcome", outcome),
                    new("error_type", errorType ?? "none"));

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.AgentTimeoutSeconds)));
        try
        {
                McpInvocationContext? mcpContext;
                try
                {
                    mcpContext = await CreateInvocationContextAsync(agent, executionContext, timeout.Token);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    const string errorCode = "research.mcp_context_read_failed";
                    LogContextReadFailure(logger, agent.Id, Activity.Current?.TraceId.ToString(), exception);
                    await PublishActivityAsync(executionContext, agent, OrchestrationEventStatus.Failed,
                        "Orchestration could not read shared agent context through MCP.", errorCode, true,
                        Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, CancellationToken.None);
                    RecordDispatch("Failed", errorCode);
                    return new AgentResult(null, errorCode, true);
                }

                var response = await dispatcher.DispatchAsync(agent, request, mcpContext?.InvocationContext, timeout.Token);
                if (mcpContext is not null && response.Outcome is ResearchOutcome.Completed or ResearchOutcome.Degraded)
                {
                    response = await AppendResearchContextAsync(agent, response, mcpContext, timeout.Token);
                }
                if (response.Outcome == ResearchOutcome.Failed)
                {
                    LogAgentReportedFailure(
                        logger,
                        agent.Id,
                        response.ErrorCode ?? "research.agent_failed",
                        Activity.Current?.TraceId.ToString());
                }

                var status = response.Outcome switch
                {
                    ResearchOutcome.Completed => OrchestrationEventStatus.Completed,
                    ResearchOutcome.Degraded => OrchestrationEventStatus.Degraded,
                    ResearchOutcome.Skipped => OrchestrationEventStatus.Skipped,
                    _ => OrchestrationEventStatus.Failed
                };
                await PublishActivityAsync(
                    executionContext,
                    agent,
                    status,
                    response.Outcome switch
                    {
                        ResearchOutcome.Failed => "Agent research failed; orchestration will continue when another result is available.",
                        ResearchOutcome.Skipped => "Agent was skipped because its required input was unavailable.",
                        _ => $"Agent returned {response.Facts.Count} cited facts from {response.Sources.Count} sources."
                    },
                    response.ErrorCode,
                    response.Retryable,
                    Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                    CancellationToken.None);

                RecordDispatch(response.Outcome.ToString(),
                    response.Outcome == ResearchOutcome.Failed ? response.ErrorCode ?? "research.agent_failed" : response.ErrorCode);
                return response.Outcome == ResearchOutcome.Failed
                    ? new AgentResult(null, response.ErrorCode ?? "research.agent_failed", response.Retryable)
                    : new AgentResult(response, null, false);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                LogAgentFailure(logger, agent.Id, "research.agent_timeout", exception, Activity.Current?.TraceId.ToString());
                await PublishActivityAsync(executionContext, agent, OrchestrationEventStatus.Failed,
                    "Agent research timed out.", "research.agent_timeout", true,
                    Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, CancellationToken.None);
                RecordDispatch("Failed", "research.agent_timeout");
                return new AgentResult(null, "research.agent_timeout", true);
            }
            catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
            {
                const string errorCode = "research.request_cancelled";
                LogAgentFailure(logger, agent.Id, errorCode, exception, Activity.Current?.TraceId.ToString());
                await PublishActivityAsync(executionContext, agent, OrchestrationEventStatus.Failed,
                    "Agent research was cancelled because the onboarding request ended.", errorCode, true,
                    Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, CancellationToken.None);
                RecordDispatch("Cancelled", errorCode);
                throw;
            }
            catch (Exception exception)
            {
                LogAgentFailure(logger, agent.Id, "research.agent_failed", exception, Activity.Current?.TraceId.ToString());
                await PublishActivityAsync(executionContext, agent, OrchestrationEventStatus.Failed,
                    "Agent research failed unexpectedly.", "research.agent_failed", true,
                    Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, CancellationToken.None);
                RecordDispatch("Failed", "research.agent_failed");
                return new AgentResult(null, "research.agent_failed", true);
        }
    }

    private static BillerResearchResponse Failed(string code, bool retryable) =>
        new(ResearchOutcome.Failed, [], [], [code], code, retryable);

    private async Task<McpInvocationContext?> CreateInvocationContextAsync(
        ResearchAgentDescriptor agent,
        ResearchExecutionContext? executionContext,
        CancellationToken cancellationToken)
    {
        if (!_optionsForMcp.Enabled || executionContext is null)
        {
            return null;
        }
        if (capabilityIssuer is null || contextGateway is null)
        {
            throw new InvalidOperationException(
                "MCP context orchestration is enabled but its capability issuer or gateway is unavailable.");
        }

        var token = capabilityIssuer.Issue(executionContext.BillerId, executionContext.RunId, agent.Id, canWrite: true);
        var snapshot = await contextGateway.GetAsync(token, cancellationToken);
        return new McpInvocationContext(token, snapshot, new ResearchAgentInvocationContext(snapshot));
    }

    private async Task<BillerResearchResponse> AppendResearchContextAsync(
        ResearchAgentDescriptor agent,
        BillerResearchResponse response,
        McpInvocationContext context,
        CancellationToken cancellationToken)
    {
        if (contextGateway is null || response.Sources.Count == 0) return response;

        var sources = response.Sources.Select(source => source.Url).Distinct().Take(20).ToArray();
        var conclusions = string.Join("; ", response.Facts.Take(12)
            .Select(fact => $"{fact.Name}: {fact.Value}"));
        var content = conclusions.Length <= 3_900 ? conclusions : conclusions[..3_900];
        if (string.IsNullOrWhiteSpace(content)) return response;

        var request = new AppendAgentContextRequest(
            context.Snapshot.Version,
            AgentContextEntryKind.CandidateArtifact,
            agent.Id,
            "research",
            content,
            sources,
            External: true);
        try
        {
            await contextGateway.AppendAsync(context.CapabilityToken, request, cancellationToken);
            return response;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogContextWriteFailure(logger, agent.Id, retrying: true, Activity.Current?.TraceId.ToString(), exception);
        }

        try
        {
            var latest = await contextGateway.GetAsync(context.CapabilityToken, cancellationToken);
            await contextGateway.AppendAsync(
                context.CapabilityToken,
                request with { ExpectedVersion = latest.Version },
                cancellationToken);
            return response;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogContextWriteFailure(logger, agent.Id, retrying: false, Activity.Current?.TraceId.ToString(), exception);
            const string warning = "research.mcp_context_write_failed";
            return response with
            {
                Outcome = ResearchOutcome.Degraded,
                Warnings = response.Warnings.Append(warning).Distinct(StringComparer.Ordinal).ToArray()
            };
        }
    }

    private bool IsEligible(ResearchAgentDescriptor agent, string[] allowedAgentIds) =>
        agent.Approved &&
        agent.Enabled &&
        (allowedAgentIds.Length == 0 || allowedAgentIds.Contains(agent.Id, StringComparer.OrdinalIgnoreCase)) &&
        agent.Capabilities.Any(capability =>
            capability.Equals(_options.RequiredCapability, StringComparison.OrdinalIgnoreCase));

    private string DescribeIneligibility(ResearchAgentDescriptor agent, string[] allowedAgentIds)
    {
        var reasons = new List<string>();
        if (!agent.Approved) reasons.Add("not approved");
        if (!agent.Enabled) reasons.Add("disabled");
        if (allowedAgentIds.Length > 0 && !allowedAgentIds.Contains(agent.Id, StringComparer.OrdinalIgnoreCase))
            reasons.Add("not allowlisted");
        if (!agent.Capabilities.Any(capability =>
                capability.Equals(_options.RequiredCapability, StringComparison.OrdinalIgnoreCase)))
            reasons.Add($"missing capability {_options.RequiredCapability}");
        return $"Available in the {agent.Provider} inventory; not invoked ({string.Join(", ", reasons)}).";
    }

    private async ValueTask PublishActivityAsync(
        ResearchExecutionContext? executionContext,
        ResearchAgentDescriptor agent,
        OrchestrationEventStatus status,
        string summary,
        string? errorCode = null,
        bool retryable = false,
        double? durationMs = null,
        CancellationToken cancellationToken = default)
    {
        if (executionContext is null) return;
        try
        {
            await executionContext.ActivitySink.PublishAsync(new OrchestrationEvent(
                Guid.NewGuid().ToString("N"),
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                executionContext.RunId,
                agent.Id,
                agent.DisplayName,
                status,
                summary,
                DateTimeOffset.UtcNow,
                Activity.Current?.TraceId.ToString(),
                errorCode,
                retryable,
                DurationMs: durationMs), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogActivityFailure(logger, agent.Id, status.ToString(), Activity.Current?.TraceId.ToString(), exception);
        }
    }

    private sealed record AgentResult(BillerResearchResponse? Response, string? ErrorCode, bool Retryable);
    private sealed record McpInvocationContext(
        string CapabilityToken,
        AgentContextSnapshot Snapshot,
        ResearchAgentInvocationContext InvocationContext);

    [LoggerMessage(2650, LogLevel.Error, "Research agent catalog discovery failed; trace {TraceId}")]
    private static partial void LogCatalogFailure(ILogger logger, Exception exception, string? traceId);

    [LoggerMessage(2651, LogLevel.Error, "No approved research agents were eligible among {DiscoveredCount} discovered agents; trace {TraceId}")]
    private static partial void LogNoEligibleAgents(ILogger logger, int discoveredCount, string? traceId);

    [LoggerMessage(2652, LogLevel.Error, "Research agent {AgentId} failed with {ErrorCode}; trace {TraceId}")]
    private static partial void LogAgentFailure(ILogger logger, string agentId, string errorCode, Exception exception, string? traceId);

    [LoggerMessage(2653, LogLevel.Error, "Research agent {AgentId} returned failed outcome {ErrorCode}; trace {TraceId}")]
    private static partial void LogAgentReportedFailure(ILogger logger, string agentId, string errorCode, string? traceId);

    [LoggerMessage(2654, LogLevel.Error, "Research coordinator consolidation failed with {ErrorCode}; trace {TraceId}")]
    private static partial void LogConsolidationFailure(ILogger logger, string errorCode, string? traceId);

    [LoggerMessage(2659, LogLevel.Warning, "Research completed degraded with operational warnings {Warnings}; trace {TraceId}")]
    private static partial void LogDegraded(ILogger logger, string warnings, string? traceId);

    [LoggerMessage(2660, LogLevel.Information, "Research completed with advisory data-quality warnings {Warnings}; trace {TraceId}")]
    private static partial void LogAdvisoryWarnings(ILogger logger, string warnings, string? traceId);

    [LoggerMessage(2655, LogLevel.Error, "Research coordinator consolidation threw an unexpected error; trace {TraceId}")]
    private static partial void LogConsolidationException(ILogger logger, string? traceId, Exception exception);

    [LoggerMessage(2656, LogLevel.Error, "Publishing research activity for agent {AgentId} status {Status} failed; trace {TraceId}")]
    private static partial void LogActivityFailure(ILogger logger, string agentId, string status, string? traceId, Exception exception);

    [LoggerMessage(2657, LogLevel.Error, "Orchestration failed to read MCP context for research agent {AgentId}; trace {TraceId}")]
    private static partial void LogContextReadFailure(ILogger logger, string agentId, string? traceId, Exception exception);

    [LoggerMessage(2658, LogLevel.Error, "Orchestration failed to append MCP context for research agent {AgentId}; retrying {Retrying}; trace {TraceId}")]
    private static partial void LogContextWriteFailure(ILogger logger, string agentId, bool retrying, string? traceId, Exception exception);
}
