using Pronto.BillerExperience.Api.Configuration;
using Pronto.BillerExperience.Api.Infrastructure.Research;
using Pronto.Agentic.Orchestration.Abstractions;
using Pronto.BillerExperience.Contracts.V1.AgentContext;
using Pronto.BillerExperience.Contracts.V1.Research;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Pronto.BillerExperience.Api.Tests;

public sealed class BillerResearchCoordinatorTests
{
    [Fact]
    public async Task ResearchDispatchesOnlyAllowlistedCapableAgentsAndConsolidatesCitations()
    {
        var catalog = new StubCatalog([
            Agent("approved", "biller_research"),
            Agent("not-approved", "biller_research"),
            Agent("wrong-capability", "design")]);
        var dispatcher = new StubDispatcher((agent, _) => Completed(agent.Id));
        var coordinator = Create(catalog, dispatcher, ["approved"]);

        var response = await coordinator.ResearchAsync(Request());

        Assert.Equal(ResearchOutcome.Completed, response.Outcome);
        Assert.Equal(["approved"], dispatcher.Dispatched);
        Assert.Single(response.Sources);
        Assert.Single(response.Facts);
    }

    [Fact]
    public async Task BuiltInLocalWorkerIsNotExcludedByFoundryAllowlist()
    {
        var catalog = new StubCatalog([
            Agent("same-site-research", "biller_research") with { Provider = "local" },
            Agent("foundry-worker", "biller_research") with { Provider = "foundry" },
            Agent("other-foundry-worker", "biller_research") with { Provider = "foundry" }]);
        var dispatcher = new StubDispatcher((agent, _) => Completed(agent.Id));
        var coordinator = Create(catalog, dispatcher, ["foundry-worker"]);

        await coordinator.ResearchAsync(Request());

        Assert.Contains("same-site-research", dispatcher.Dispatched);
        Assert.Contains("foundry-worker", dispatcher.Dispatched);
        Assert.DoesNotContain("other-foundry-worker", dispatcher.Dispatched);
    }

    [Fact]
    public async Task ResearchReturnsDegradedResultWhenOneAgentFails()
    {
        var catalog = new StubCatalog([Agent("one", "biller_research"), Agent("two", "biller_research")]);
        var dispatcher = new StubDispatcher((agent, _) => agent.Id == "one"
            ? Completed("shared")
            : throw new InvalidOperationException("provider detail"));
        var coordinator = Create(catalog, dispatcher, ["one", "two"]);

        var response = await coordinator.ResearchAsync(Request());

        Assert.Equal(ResearchOutcome.Degraded, response.Outcome);
        Assert.Contains("research.agent_failed", response.Warnings);
        Assert.Single(response.Facts);
    }

    [Fact]
    public async Task AdvisoryDataQualityWarningDoesNotDegradeRun()
    {
        var catalog = new StubCatalog([Agent("one", "biller_research")]);
        var dispatcher = new StubDispatcher((agent, _) => CompletedWith(agent.Id, ResearchOutcome.Degraded,
            "conflicting phone numbers found", "some values unverifiable"));
        var coordinator = Create(catalog, dispatcher, ["one"]);

        var response = await coordinator.ResearchAsync(Request());

        Assert.Equal(ResearchOutcome.Completed, response.Outcome);
        Assert.Null(response.ErrorCode);
        Assert.Contains("conflicting phone numbers found", response.Warnings);
        Assert.Single(response.Facts);
    }

    [Fact]
    public async Task OperationalWarningStillDegradesAndSurfacesErrorCode()
    {
        var catalog = new StubCatalog([Agent("one", "biller_research"), Agent("two", "biller_research")]);
        var dispatcher = new StubDispatcher((agent, _) => agent.Id == "one"
            ? Completed("shared")
            : throw new InvalidOperationException("provider detail"));
        var coordinator = Create(catalog, dispatcher, ["one", "two"]);

        var response = await coordinator.ResearchAsync(Request());

        Assert.Equal(ResearchOutcome.Degraded, response.Outcome);
        Assert.Equal("research.agent_failed", response.ErrorCode);
        Assert.Contains("research.agent_failed", response.Warnings);
    }

    [Fact]
    public async Task NoCitedFactsFromOneAgentDoesNotDegradeRun()
    {
        var catalog = new StubCatalog([Agent("one", "biller_research"), Agent("two", "biller_research")]);
        var dispatcher = new StubDispatcher((agent, _) => agent.Id == "one"
            ? Completed("shared")
            : CompletedWith(agent.Id, ResearchOutcome.Skipped, "research.no_cited_facts",
                "No verified first-party website was identified."));
        var coordinator = Create(catalog, dispatcher, ["one", "two"]);

        var response = await coordinator.ResearchAsync(Request());

        Assert.Equal(ResearchOutcome.Completed, response.Outcome);
        Assert.Null(response.ErrorCode);
        Assert.Contains("research.no_cited_facts", response.Warnings);
    }

    [Fact]
    public async Task ConsolidationAdvisoryWarningDoesNotDegradeRun()
    {
        var catalog = new StubCatalog([Agent("one", "biller_research"), Agent("two", "biller_research")]);
        var dispatcher = new StubDispatcher((agent, _) => Completed(agent.Id));
        var consolidator = new StubConsolidator(CompletedWith("consolidated", ResearchOutcome.Completed,
            "low confidence in mailing address"));
        var coordinator = Create(catalog, dispatcher, ["one", "two"], consolidator);

        var response = await coordinator.ResearchAsync(Request());

        Assert.Equal(ResearchOutcome.Completed, response.Outcome);
        Assert.Null(response.ErrorCode);
        Assert.Contains("low confidence in mailing address", response.Warnings);
    }

    [Fact]
    public async Task ResearchUsesCoordinatorAgentToConsolidateSwarmResults()
    {
        var catalog = new StubCatalog([Agent("one", "biller_research"), Agent("two", "biller_research")]);
        var dispatcher = new StubDispatcher((agent, _) => Completed(agent.Id));
        var consolidator = new StubConsolidator(Completed("consolidated"));
        var coordinator = Create(catalog, dispatcher, ["one", "two"], consolidator);
        var sink = new RecordingSink();

        var response = await coordinator.ResearchAsync(
            Request(),
            ExecutionContext(sink));

        Assert.True(consolidator.Called);
        Assert.Contains(response.Facts, fact => fact.Value == "consolidated");
        Assert.Contains(sink.Events, item => item.AgentId == "research-coordinator" && item.Status == OrchestrationEventStatus.Discovered);
        Assert.Contains(sink.Events, item => item.AgentId == "research-coordinator" && item.Status == OrchestrationEventStatus.Running);
        Assert.Contains(sink.Events, item => item.AgentId == "research-coordinator" && item.Status == OrchestrationEventStatus.Completed && item.DurationMs >= 0);
    }

    [Fact]
    public async Task ConsolidationCannotReplaceDeterministicFirstPartyBrandEvidence()
    {
        var source = new Uri("https://example.com/site.css");
        var local = new BillerResearchResponse(
            ResearchOutcome.Completed,
            [new ResearchFact(BrandEvidenceFacts.PrimaryColor, "#123456", source, 0.9)],
            [new ResearchSource(source, null, DateTimeOffset.UtcNow)],
            []);
        var consolidated = new BillerResearchResponse(
            ResearchOutcome.Completed,
            [new ResearchFact(BrandEvidenceFacts.PrimaryColor, "#abcdef", new Uri("https://search.example/"), 0.8)],
            [new ResearchSource(new Uri("https://search.example/"), null, DateTimeOffset.UtcNow)],
            []);
        var dispatcher = new StubDispatcher((agent, _) => agent.Provider == "local" ? local : Completed(agent.Id));
        var coordinator = Create(
            new StubCatalog([
                Agent("same-site-research", "biller_research") with { Provider = "local" },
                Agent("foundry-worker", "biller_research") with { Provider = "foundry" }]),
            dispatcher,
            [],
            new StubConsolidator(consolidated));

        var response = await coordinator.ResearchAsync(Request());

        var color = Assert.Single(response.Facts, fact => fact.Name == BrandEvidenceFacts.PrimaryColor);
        Assert.Equal("#123456", color.Value);
        Assert.Equal(source, color.SourceUrl);
        Assert.Contains(response.Sources, item => item.Url == source);
    }

    [Fact]
    public async Task ResearchPublishesActualDiscoveryAndInvocationActivity()
    {
        var catalog = new StubCatalog([
            Agent("eligible", "biller_research") with { Provider = "foundry" },
            Agent("rejected", "biller_research") with { Provider = "foundry", Approved = false }]);
        var dispatcher = new StubDispatcher((agent, _) => Completed(agent.Id));
        var coordinator = Create(catalog, dispatcher, []);
        var sink = new RecordingSink();

        await coordinator.ResearchAsync(Request(), ExecutionContext(sink));

        Assert.Contains(sink.Events, item => item.AgentId == "eligible" && item.Status == OrchestrationEventStatus.Discovered);
        Assert.Contains(sink.Events, item => item.AgentId == "eligible" && item.Status == OrchestrationEventStatus.Running);
        Assert.Contains(sink.Events, item => item.AgentId == "eligible" && item.Status == OrchestrationEventStatus.Completed && item.DurationMs >= 0);
        Assert.DoesNotContain(sink.Events, item => item.AgentId == "rejected");
    }

    [Fact]
    public async Task RequestCancellationPublishesTerminalFailureForRunningAgent()
    {
        var sink = new RecordingSink();
        var dispatcher = new CancellingDispatcher();
        var coordinator = Create(
            new StubCatalog([Agent("worker", "biller_research")]),
            dispatcher,
            []);
        using var cancellation = new CancellationTokenSource();

        var research = coordinator.ResearchAsync(
            Request(),
            ExecutionContext(sink),
            cancellation.Token);
        await dispatcher.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => research);

        Assert.Contains(sink.Events, item =>
            item.AgentId == "worker" &&
            item.Status == OrchestrationEventStatus.Failed &&
            item.ErrorCode == "research.request_cancelled" &&
            item.Retryable);
    }

    [Fact]
    public async Task OrchestrationUsesContextRunIdForMcpAndExecutionIdForActivity()
    {
        var dispatcher = new StubDispatcher((agent, _) => Completed(agent.Id));
        var issuer = new StubCapabilityIssuer();
        var gateway = new StubContextGateway();
        var sink = new RecordingSink();
        var coordinator = Create(
            new StubCatalog([Agent("worker", "biller_research")]),
            dispatcher,
            [],
            capabilityIssuer: issuer,
            contextGateway: gateway,
            mcpEndpoint: "http://demo.example/api/mcp");

        await coordinator.ResearchAsync(
            Request(),
            ExecutionContext(sink, executionId: "turn-42", contextRunId: "onboarding"));

        Assert.Equal(("biller-1", "onboarding", "worker", true), issuer.Issued);
        Assert.NotEmpty(sink.Events);
        Assert.All(sink.Events, item => Assert.Equal("turn-42", item.RunId));
        var context = Assert.Single(dispatcher.InvocationContexts);
        Assert.Equal("biller-1", context!.SharedContext.BillerId);
        Assert.Equal(4, context.SharedContext.Version);
        Assert.Equal("scoped-token", gateway.GetToken);
        Assert.Equal("scoped-token", gateway.AppendToken);
        Assert.Equal(4, gateway.Appended!.ExpectedVersion);
        Assert.Equal(AgentContextEntryKind.CandidateArtifact, gateway.Appended.Kind);
        Assert.Equal("research", gateway.Appended.Scope);
    }

    [Fact]
    public async Task McpContextReadFailureStopsAgentWithSpecificRetryableError()
    {
        var dispatcher = new StubDispatcher((agent, _) => Completed(agent.Id));
        var coordinator = Create(
            new StubCatalog([Agent("worker", "biller_research")]),
            dispatcher,
            [],
            capabilityIssuer: new StubCapabilityIssuer(),
            contextGateway: new FailingReadContextGateway(),
            mcpEndpoint: "http://demo.example/api/mcp");

        var response = await coordinator.ResearchAsync(
            Request(),
            ExecutionContext(new RecordingSink()));

        Assert.Equal(ResearchOutcome.Failed, response.Outcome);
        Assert.Equal("research.mcp_context_read_failed", response.ErrorCode);
        Assert.True(response.Retryable);
        Assert.Empty(dispatcher.Dispatched);
    }

    [Fact]
    public async Task McpContextWriteFailurePreservesResearchAsDegradedAfterRetry()
    {
        var gateway = new FailingWriteContextGateway();
        var coordinator = Create(
            new StubCatalog([Agent("worker", "biller_research")]),
            new StubDispatcher((agent, _) => Completed(agent.Id)),
            [],
            capabilityIssuer: new StubCapabilityIssuer(),
            contextGateway: gateway,
            mcpEndpoint: "http://demo.example/api/mcp");

        var response = await coordinator.ResearchAsync(
            Request(),
            ExecutionContext(new RecordingSink()));

        Assert.Equal(ResearchOutcome.Degraded, response.Outcome);
        Assert.Contains("research.mcp_context_write_failed", response.Warnings);
        Assert.Single(response.Facts);
        Assert.Equal(3, gateway.GetCount);
        Assert.Equal(2, gateway.AppendCount);
    }

    [Fact]
    public async Task FoundryAgentCanResearchBillerWithoutWebsite()
    {
        var dispatcher = new StubDispatcher((agent, request) =>
        {
            Assert.Null(request.Website);
            Assert.Equal("Example Water", request.BillerName);
            return Completed(agent.Id);
        });
        var coordinator = Create(
            new StubCatalog([Agent("foundry-worker", "biller_research") with { Provider = "foundry" }]),
            dispatcher,
            []);

        var response = await coordinator.ResearchAsync(
            new BillerResearchRequest(null, "brand", BillerName: "Example Water", BillType: "Utility"));

        Assert.Equal(ResearchOutcome.Completed, response.Outcome);
        Assert.Equal(["foundry-worker"], dispatcher.Dispatched);
    }

    [Fact]
    public async Task SingleAgentFailurePreservesRootErrorAndRetryability()
    {
        var dispatcher = new StubDispatcher((_, _) => new BillerResearchResponse(
            ResearchOutcome.Failed,
            [],
            [],
            ["research.foundry_invalid_output"],
            "research.foundry_invalid_output",
            false));
        var coordinator = Create(
            new StubCatalog([Agent("foundry-worker", "biller_research") with { Provider = "foundry" }]),
            dispatcher,
            []);

        var response = await coordinator.ResearchAsync(Request());

        Assert.Equal(ResearchOutcome.Failed, response.Outcome);
        Assert.Equal("research.foundry_invalid_output", response.ErrorCode);
        Assert.False(response.Retryable);
    }

    private static BillerResearchCoordinator Create(
        IResearchAgentCatalog catalog,
        IResearchAgentDispatcher dispatcher,
        string[] allowlist,
        IFoundryResearchConsolidator? consolidator = null,
        IAgentContextCapabilityIssuer? capabilityIssuer = null,
        IAgentContextMcpGateway? contextGateway = null,
        string? mcpEndpoint = null) => new(
            catalog,
            dispatcher,
            Options.Create(new BillerExperienceOptions
            {
                Research = new ResearchOptions
                {
                    AllowedAgentIds = allowlist,
                    MaxAgentCount = 4,
                    MaxParallelAgents = 2,
                    AgentTimeoutSeconds = 2
                },
                Mcp = new McpOptions
                {
                    Enabled = mcpEndpoint is not null,
                    PublicEndpoint = mcpEndpoint ?? string.Empty
                }
            }),
            NullLogger<BillerResearchCoordinator>.Instance,
            consolidator,
            capabilityIssuer,
            contextGateway);

    private static BillerResearchRequest Request() => new(new Uri("https://example.com"), "brand");
    private static ResearchExecutionContext ExecutionContext(
        IOrchestrationEventSink sink,
        string executionId = "run-1",
        string contextRunId = "run-1") => new(
        BillerId: "biller-1",
        ExecutionId: executionId,
        ContextRunId: contextRunId,
        ActivitySink: sink);
    private static ResearchAgentDescriptor Agent(string id, string capability) => new(id, id, new HashSet<string> { capability });

    private static BillerResearchResponse Completed(string value)
    {
        var uri = new Uri("https://example.com");
        return new BillerResearchResponse(
            ResearchOutcome.Completed,
            [new ResearchFact("name", value, uri, 0.9)],
            [new ResearchSource(uri, "Example", DateTimeOffset.UtcNow)],
            []);
    }

    private static BillerResearchResponse CompletedWith(string value, ResearchOutcome outcome, params string[] warnings)
        => Completed(value) with { Outcome = outcome, Warnings = warnings };

    private sealed class StubCatalog(IReadOnlyList<ResearchAgentDescriptor> agents) : IResearchAgentCatalog
    {
        public Task<IReadOnlyList<ResearchAgentDescriptor>> ListAsync(CancellationToken cancellationToken) => Task.FromResult(agents);
    }

    private sealed class StubDispatcher(Func<ResearchAgentDescriptor, BillerResearchRequest, BillerResearchResponse> dispatch) : IResearchAgentDispatcher
    {
        public List<string> Dispatched { get; } = [];
        public List<ResearchAgentInvocationContext?> InvocationContexts { get; } = [];

        public Task<BillerResearchResponse> DispatchAsync(
            ResearchAgentDescriptor agent,
            BillerResearchRequest request,
            ResearchAgentInvocationContext? invocationContext,
            CancellationToken cancellationToken)
        {
            Dispatched.Add(agent.Id);
            InvocationContexts.Add(invocationContext);
            return Task.FromResult(dispatch(agent, request));
        }
    }

    private sealed class CancellingDispatcher : IResearchAgentDispatcher
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<BillerResearchResponse> DispatchAsync(
            ResearchAgentDescriptor agent,
            BillerResearchRequest request,
            ResearchAgentInvocationContext? invocationContext,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The cancellation-aware delay unexpectedly completed.");
        }
    }

    private sealed class StubCapabilityIssuer : IAgentContextCapabilityIssuer
    {
        public (string BillerId, string RunId, string AgentId, bool CanWrite)? Issued { get; private set; }

        public string Issue(string billerId, string runId, string agentId, bool canWrite)
        {
            Issued = (billerId, runId, agentId, canWrite);
            return "scoped-token";
        }
    }

    private sealed class StubContextGateway : IAgentContextMcpGateway
    {
        public string? GetToken { get; private set; }
        public string? AppendToken { get; private set; }
        public AppendAgentContextRequest? Appended { get; private set; }

        public Task<AgentContextSnapshot> GetAsync(string capabilityToken, CancellationToken cancellationToken)
        {
            GetToken = capabilityToken;
            return Task.FromResult(new AgentContextSnapshot(
                "biller-1", "run-1", 4, "Build a payment experience", [], DateTimeOffset.UtcNow));
        }

        public Task<AgentContextSnapshot> AppendAsync(
            string capabilityToken,
            AppendAgentContextRequest request,
            CancellationToken cancellationToken)
        {
            AppendToken = capabilityToken;
            Appended = request;
            return Task.FromResult(new AgentContextSnapshot(
                "biller-1", "run-1", request.ExpectedVersion + 1, "Build a payment experience", [], DateTimeOffset.UtcNow));
        }
    }

    private sealed class FailingReadContextGateway : IAgentContextMcpGateway
    {
        public Task<AgentContextSnapshot> GetAsync(string capabilityToken, CancellationToken cancellationToken) =>
            Task.FromException<AgentContextSnapshot>(new HttpRequestException("MCP unavailable"));

        public Task<AgentContextSnapshot> AppendAsync(
            string capabilityToken,
            AppendAgentContextRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FailingWriteContextGateway : IAgentContextMcpGateway
    {
        public int GetCount { get; private set; }
        public int AppendCount { get; private set; }

        public Task<AgentContextSnapshot> GetAsync(string capabilityToken, CancellationToken cancellationToken)
        {
            GetCount++;
            return Task.FromResult(new AgentContextSnapshot(
                "biller-1", "run-1", GetCount + 2, "Build a payment experience", [], DateTimeOffset.UtcNow));
        }

        public Task<AgentContextSnapshot> AppendAsync(
            string capabilityToken,
            AppendAgentContextRequest request,
            CancellationToken cancellationToken)
        {
            AppendCount++;
            return Task.FromException<AgentContextSnapshot>(new InvalidOperationException("version conflict"));
        }
    }

    private sealed class StubConsolidator(BillerResearchResponse response) : IFoundryResearchConsolidator
    {
        public bool Called { get; private set; }

        public Task<BillerResearchResponse> ConsolidateAsync(
            BillerResearchRequest request,
            IReadOnlyList<BillerResearchResponse> results,
            ResearchExecutionContext? executionContext,
            CancellationToken cancellationToken)
        {
            Called = true;
            Assert.Equal(2, results.Count);
            return Task.FromResult(response);
        }
    }

    private sealed class RecordingSink : IOrchestrationEventSink
    {
        public List<OrchestrationEvent> Events { get; } = [];

        public ValueTask PublishAsync(OrchestrationEvent activity, CancellationToken cancellationToken = default)
        {
            Events.Add(activity);
            return ValueTask.CompletedTask;
        }
    }
}
