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
    IAgentContextMcpGateway? contextGateway = null) : IBillerResearchCoordinator, IDisposable
{
    private readonly ResearchOptions _options = options.Value.Research;
    private readonly McpOptions _optionsForMcp = options.Value.Mcp;
    private readonly SemaphoreSlim[] _contextWriteGates = Enumerable.Range(0, 32)
        .Select(_ => new SemaphoreSlim(1, 1))
        .ToArray();

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

        var eligibility = discovered
            .Select(agent => new { Agent = agent, Reason = IneligibilityReason(agent, _options.AllowedAgentIds) })
            .ToArray();
        var eligible = eligibility.Where(item => item.Reason is null).Select(item => item.Agent).ToArray();
        var agents = eligible
            .Take(Math.Max(1, _options.MaxAgentCount))
            .ToArray();

        var exclusionCounts = eligibility
            .Where(item => item.Reason is not null)
            .GroupBy(item => item.Reason!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        if (eligible.Length > agents.Length)
        {
            exclusionCounts["agent_limit"] = eligible.Length - agents.Length;
        }
        foreach (var exclusion in exclusionCounts)
        {
            ResearchTelemetry.AgentExclusions.Add(exclusion.Value,
                new KeyValuePair<string, object?>("reason", exclusion.Key));
            activity?.SetTag($"research.agent.excluded.{exclusion.Key}", exclusion.Value);
        }
        if (exclusionCounts.Count > 0)
        {
            LogAgentExclusions(logger,
                string.Join(", ", exclusionCounts.OrderBy(item => item.Key).Select(item => $"{item.Key}={item.Value}")),
                Activity.Current?.TraceId.ToString());
        }

        LogAgentSelection(
            logger,
            discovered.Count,
            agents.Length,
            discovered.Count - agents.Length,
            Activity.Current?.TraceId.ToString());

        foreach (var agent in agents)
        {
            await PublishActivityAsync(
                executionContext,
                agent,
                OrchestrationEventStatus.Discovered,
                $"Selected approved {agent.Provider} agent with capability {_options.RequiredCapability}.",
                cancellationToken: cancellationToken);
        }

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
        var deterministic = agents.Select((agent, index) => new { agent, results[index].Response })
            .FirstOrDefault(item => item.agent.Provider.Equals("local", StringComparison.OrdinalIgnoreCase))
            ?.Response;
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
            var coordinatorAgent = CoordinatorAgent();
            await PublishActivityAsync(
                executionContext,
                coordinatorAgent,
                OrchestrationEventStatus.Discovered,
                "Selected Foundry coordinator to consolidate research results.",
                cancellationToken: cancellationToken);
            await PublishActivityAsync(
                executionContext,
                coordinatorAgent,
                OrchestrationEventStatus.Running,
                $"Consolidating results from {successful.Length} research agents.",
                cancellationToken: cancellationToken);
            var consolidationStartedAt = Stopwatch.GetTimestamp();
            try
            {
                var consolidated = await consolidator.ConsolidateAsync(request, successful, executionContext, cancellationToken);
                if (consolidated.Outcome != ResearchOutcome.Failed)
                {
                    consolidated = PreserveDeterministicBrandEvidence(consolidated, deterministic);
                    await PublishActivityAsync(
                        executionContext,
                        coordinatorAgent,
                        consolidated.Outcome == ResearchOutcome.Degraded
                            ? OrchestrationEventStatus.Degraded
                            : OrchestrationEventStatus.Completed,
                        $"Consolidated {successful.Length} research results.",
                        consolidated.ErrorCode,
                        consolidated.Retryable,
                        Stopwatch.GetElapsedTime(consolidationStartedAt).TotalMilliseconds,
                        CancellationToken.None);
                    activity?.SetTag("research.consolidated", true);
                    activity?.SetTag("research.agent.succeeded", successful.Length);
                    var consolidatedWarnings = consolidated.Warnings.Concat(warnings)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();
                    return Record(activity, startedAt, FinalizeOutcome(consolidated with { Warnings = consolidatedWarnings }));
                }

                var code = consolidated.ErrorCode ?? "research.consolidation_failed";
                await PublishActivityAsync(
                    executionContext,
                    coordinatorAgent,
                    OrchestrationEventStatus.Failed,
                    "Research consolidation failed; using the merged worker results.",
                    code,
                    consolidated.Retryable,
                    Stopwatch.GetElapsedTime(consolidationStartedAt).TotalMilliseconds,
                    CancellationToken.None);
                LogConsolidationFailure(logger, code, Activity.Current?.TraceId.ToString());
                warnings.Add(code);
            }
            catch (OperationCanceledException)
            {
                await PublishActivityAsync(
                    executionContext,
                    coordinatorAgent,
                    OrchestrationEventStatus.Failed,
                    "Research consolidation was cancelled because the onboarding request ended.",
                    "research.request_cancelled",
                    true,
                    Stopwatch.GetElapsedTime(consolidationStartedAt).TotalMilliseconds,
                    CancellationToken.None);
                throw;
            }
            catch (Exception exception)
            {
                await PublishActivityAsync(
                    executionContext,
                    coordinatorAgent,
                    OrchestrationEventStatus.Failed,
                    "Research consolidation failed unexpectedly; using the merged worker results.",
                    "research.consolidation_failed",
                    true,
                    Stopwatch.GetElapsedTime(consolidationStartedAt).TotalMilliseconds,
                    CancellationToken.None);
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

    // "research.*" codes that describe a legitimate empty/skip outcome rather than a system fault.
    // "We searched and found no citable first-party source" is not a degradation — the agents ran
    // fine, there was simply nothing to cite — so these do not flip the badge to Degraded.
    private static readonly HashSet<string> NonDegradingResearchCodes = new(StringComparer.Ordinal)
    {
        "research.no_cited_facts",
        "research.skipped",
        "research.agent_ineligible",
        "research.not_configured",
    };

    // A run is degraded only when an operational warning is present. Operational warnings are the
    // orchestration's own "research.*" codes for genuine system faults (an agent failed, timed out,
    // consolidation or MCP context failed) — excluding the no-evidence/skip outcomes above. Free-form
    // data-quality caveats an agent writes into its own result are advisory and are surfaced without
    // flipping the badge, so a clean or no-evidence run reports Completed.
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
        code.StartsWith("research.", StringComparison.Ordinal) && !NonDegradingResearchCodes.Contains(code);

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

        var token = capabilityIssuer.Issue(
            executionContext.BillerId,
            executionContext.ContextRunId,
            agent.Id,
            canWrite: true);
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
        var contextKey = $"{context.Snapshot.BillerId}\n{context.Snapshot.RunId}";
        var gate = _contextWriteGates[(contextKey.GetHashCode(StringComparison.Ordinal) & int.MaxValue) % _contextWriteGates.Length];
        await gate.WaitAsync(cancellationToken);
        try
        {
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
        finally
        {
            gate.Release();
        }
    }

    private string? IneligibilityReason(ResearchAgentDescriptor agent, string[] allowedAgentIds)
    {
        if (!agent.Approved) return "not_approved";
        if (!agent.Enabled) return "disabled";
        if (!agent.Id.Equals(LocalResearchAgentCatalog.SameSiteAgent.Id, StringComparison.OrdinalIgnoreCase) &&
            allowedAgentIds.Length > 0 &&
            !allowedAgentIds.Contains(agent.Id, StringComparer.OrdinalIgnoreCase))
            return "not_allowlisted";
        return agent.Capabilities.Any(capability =>
            capability.Equals(_options.RequiredCapability, StringComparison.OrdinalIgnoreCase))
            ? null
            : "missing_capability";
    }

    private static BillerResearchResponse PreserveDeterministicBrandEvidence(
        BillerResearchResponse consolidated,
        BillerResearchResponse? deterministic)
    {
        if (deterministic is null)
        {
            return consolidated;
        }

        var localBrandFacts = deterministic.Facts
            .Where(fact => CanonicalBrandFactNames.Contains(fact.Name))
            .ToArray();
        if (localBrandFacts.Length == 0)
        {
            return consolidated;
        }

        var replacedNames = localBrandFacts.Select(fact => fact.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var facts = consolidated.Facts
            .Where(fact => !replacedNames.Contains(fact.Name))
            .Concat(localBrandFacts)
            .ToArray();
        var sources = consolidated.Sources.Concat(deterministic.Sources)
            .GroupBy(source => source.Url)
            .Select(group => group.OrderByDescending(source => source.RetrievedAt).First())
            .ToArray();
        return consolidated with { Facts = facts, Sources = sources };
    }

    private static readonly HashSet<string> CanonicalBrandFactNames = new(
        [
            BrandEvidenceFacts.DisplayName,
            BrandEvidenceFacts.PrimaryColor,
            BrandEvidenceFacts.SecondaryColor,
            BrandEvidenceFacts.LogoUrl,
            BrandEvidenceFacts.FontFamily,
            BrandEvidenceFacts.Tagline
        ],
        StringComparer.OrdinalIgnoreCase);

    private ResearchAgentDescriptor CoordinatorAgent() => new(
        string.IsNullOrWhiteSpace(_options.CoordinatorAgentId)
            ? "research-coordinator"
            : _options.CoordinatorAgentId,
        "Research Coordinator",
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "research_consolidation" },
        Provider: "foundry");

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
                executionContext.ExecutionId,
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

    public void Dispose()
    {
        foreach (var gate in _contextWriteGates)
        {
            gate.Dispose();
        }
    }

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

    [LoggerMessage(2661, LogLevel.Information, "Research selected {SelectedCount} eligible agents from {DiscoveredCount}; ignored {IgnoredCount} inventory agents; trace {TraceId}")]
    private static partial void LogAgentSelection(ILogger logger, int discoveredCount, int selectedCount, int ignoredCount, string? traceId);

    [LoggerMessage(2662, LogLevel.Information, "Research agent exclusions by reason: {Exclusions}; trace {TraceId}")]
    private static partial void LogAgentExclusions(ILogger logger, string exclusions, string? traceId);
}
