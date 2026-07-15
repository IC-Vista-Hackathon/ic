using System.Diagnostics;
using Pronto.Agentic.Orchestration.Abstractions;
using Pronto.BillerExperience.Api.Configuration;
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
    IAgentContextCapabilityIssuer? capabilityIssuer = null) : IBillerResearchCoordinator
{
    private readonly ResearchOptions _options = options.Value.Research;
    private readonly McpOptions _optionsForMcp = options.Value.Mcp;

    public async Task<BillerResearchResponse> ResearchAsync(
        BillerResearchRequest request,
        ResearchExecutionContext? executionContext = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = BillerExperienceTelemetry.Source.StartActivity("research.coordinate");
        IReadOnlyList<ResearchAgentDescriptor> discovered;
        try
        {
            discovered = await catalog.ListAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogCatalogFailure(logger, exception, Activity.Current?.TraceId.ToString());
            activity?.SetStatus(ActivityStatusCode.Error, "research.catalog_failed");
            return Failed("research.catalog_failed", retryable: true);
        }
        catch (OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "research.cancelled");
            throw;
        }

        foreach (var agent in discovered)
        {
            var eligible = IsEligible(agent, _options.AllowedAgentIds);
            await PublishActivityAsync(
                executionContext,
                agent,
                OrchestrationEventStatus.Discovered,
                eligible
                    ? $"Discovered approved {agent.Provider} agent with capability {_options.RequiredCapability}."
                    : DescribeIneligibility(agent, _options.AllowedAgentIds),
                cancellationToken: cancellationToken);
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
            activity?.SetStatus(ActivityStatusCode.Error, "research.no_eligible_agents");
            return Failed("research.no_eligible_agents", retryable: false);
        }

        activity?.SetTag("research.agent.count", agents.Length);
        var fanOut = await BoundedFanOut.ExecuteAsync<ResearchAgentDescriptor, AgentResult>(
            agents,
            Math.Max(1, _options.MaxParallelAgents),
            (agent, _, token) => new ValueTask<AgentResult>(DispatchAsync(agent, request, executionContext, token)),
            cancellationToken: cancellationToken);
        var results = fanOut.Select(item => item.Output ?? new AgentResult(null, "research.agent_failed")).ToArray();
        var successful = results.Where(result => result.Response is not null).Select(result => result.Response!).ToArray();
        var warnings = results.Where(result => result.ErrorCode is not null).Select(result => result.ErrorCode!).ToList();

        if (successful.Length == 0)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "research.all_agents_failed");
            return new BillerResearchResponse(ResearchOutcome.Failed, [], [], warnings, "research.all_agents_failed", true);
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
            activity?.SetStatus(ActivityStatusCode.Ok, skipCode);
            return new BillerResearchResponse(ResearchOutcome.Skipped, [], [], skipWarnings, skipCode);
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

        var merged = new BillerResearchResponse(
            warnings.Count == 0 && successful.Length == agents.Length ? ResearchOutcome.Completed : ResearchOutcome.Degraded,
            facts,
            sources,
            warnings);
        if (consolidator is not null && successful.Length > 1)
        {
            try
            {
                var consolidated = await consolidator.ConsolidateAsync(request, successful, executionContext, cancellationToken);
                if (consolidated.Outcome != ResearchOutcome.Failed)
                {
                    activity?.SetTag("research.consolidated", true);
                    activity?.SetTag("research.agent.succeeded", successful.Length);
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    return consolidated with
                    {
                        Outcome = warnings.Count == 0 && successful.Length == agents.Length
                            ? consolidated.Outcome
                            : ResearchOutcome.Degraded,
                        Warnings = consolidated.Warnings.Concat(warnings).Distinct(StringComparer.Ordinal).ToArray()
                    };
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
        activity?.SetStatus(ActivityStatusCode.Ok);
        return merged with
        {
            Outcome = warnings.Count == 0 ? merged.Outcome : ResearchOutcome.Degraded,
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToArray()
        };
    }

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
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.AgentTimeoutSeconds)));
        try
        {
                var invocationContext = CreateInvocationContext(agent, executionContext);
                var response = await dispatcher.DispatchAsync(agent, request, invocationContext, timeout.Token);
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

                return response.Outcome == ResearchOutcome.Failed
                    ? new AgentResult(null, response.ErrorCode ?? "research.agent_failed")
                    : new AgentResult(response, null);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                LogAgentFailure(logger, agent.Id, "research.agent_timeout", exception, Activity.Current?.TraceId.ToString());
                await PublishActivityAsync(executionContext, agent, OrchestrationEventStatus.Failed,
                    "Agent research timed out.", "research.agent_timeout", true,
                    Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, CancellationToken.None);
                return new AgentResult(null, "research.agent_timeout");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogAgentFailure(logger, agent.Id, "research.agent_failed", exception, Activity.Current?.TraceId.ToString());
                await PublishActivityAsync(executionContext, agent, OrchestrationEventStatus.Failed,
                    "Agent research failed unexpectedly.", "research.agent_failed", true,
                    Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, CancellationToken.None);
                return new AgentResult(null, "research.agent_failed");
        }
    }

    private static BillerResearchResponse Failed(string code, bool retryable) =>
        new(ResearchOutcome.Failed, [], [], [code], code, retryable);

    private ResearchAgentInvocationContext? CreateInvocationContext(
        ResearchAgentDescriptor agent,
        ResearchExecutionContext? executionContext)
    {
        if (!_optionsForMcp.Enabled || executionContext is null || capabilityIssuer is null ||
            !Uri.TryCreate(_optionsForMcp.PublicEndpoint, UriKind.Absolute, out var endpoint))
        {
            return null;
        }

        var token = capabilityIssuer.Issue(executionContext.BillerId, executionContext.RunId, agent.Id, canWrite: true);
        return new ResearchAgentInvocationContext(endpoint, token);
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
        return $"Discovered {agent.Provider} agent; not invoked ({string.Join(", ", reasons)}).";
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

    private sealed record AgentResult(BillerResearchResponse? Response, string? ErrorCode);

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

    [LoggerMessage(2655, LogLevel.Error, "Research coordinator consolidation threw an unexpected error; trace {TraceId}")]
    private static partial void LogConsolidationException(ILogger logger, string? traceId, Exception exception);

    [LoggerMessage(2656, LogLevel.Error, "Publishing research activity for agent {AgentId} status {Status} failed; trace {TraceId}")]
    private static partial void LogActivityFailure(ILogger logger, string agentId, string status, string? traceId, Exception exception);
}
