using Pronto.BillerExperience.Api.Configuration;
using Pronto.BillerExperience.Api.Infrastructure.Research;
using Pronto.Agentic.Orchestration.Abstractions;
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
    public async Task ResearchUsesCoordinatorAgentToConsolidateSwarmResults()
    {
        var catalog = new StubCatalog([Agent("one", "biller_research"), Agent("two", "biller_research")]);
        var dispatcher = new StubDispatcher((agent, _) => Completed(agent.Id));
        var consolidator = new StubConsolidator(Completed("consolidated"));
        var coordinator = Create(catalog, dispatcher, ["one", "two"], consolidator);

        var response = await coordinator.ResearchAsync(Request());

        Assert.True(consolidator.Called);
        Assert.Contains(response.Facts, fact => fact.Value == "consolidated");
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

        await coordinator.ResearchAsync(Request(), new ResearchExecutionContext("biller-1", "run-1", sink));

        Assert.Contains(sink.Events, item => item.AgentId == "eligible" && item.Status == OrchestrationEventStatus.Discovered);
        Assert.Contains(sink.Events, item => item.AgentId == "rejected" && item.Status == OrchestrationEventStatus.Discovered && item.Summary.Contains("not approved"));
        Assert.Contains(sink.Events, item => item.AgentId == "eligible" && item.Status == OrchestrationEventStatus.Running);
        Assert.Contains(sink.Events, item => item.AgentId == "eligible" && item.Status == OrchestrationEventStatus.Completed && item.DurationMs >= 0);
        Assert.DoesNotContain(sink.Events, item => item.AgentId == "rejected" && item.Status == OrchestrationEventStatus.Running);
    }

    [Fact]
    public async Task OrchestrationIssuesAgentScopedMcpContextForEachInvocation()
    {
        var dispatcher = new StubDispatcher((agent, _) => Completed(agent.Id));
        var issuer = new StubCapabilityIssuer();
        var coordinator = Create(
            new StubCatalog([Agent("worker", "biller_research")]),
            dispatcher,
            [],
            capabilityIssuer: issuer,
            mcpEndpoint: "http://demo.example/api/mcp");

        await coordinator.ResearchAsync(
            Request(),
            new ResearchExecutionContext("biller-1", "run-1", new RecordingSink()));

        Assert.Equal(("biller-1", "run-1", "worker", true), issuer.Issued);
        var context = Assert.Single(dispatcher.InvocationContexts);
        Assert.Equal(new Uri("http://demo.example/api/mcp"), context!.McpEndpoint);
        Assert.Equal("scoped-token", context.ContextCapabilityToken);
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
            capabilityIssuer);

    private static BillerResearchRequest Request() => new(new Uri("https://example.com"), "brand");
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

    private sealed class StubCapabilityIssuer : IAgentContextCapabilityIssuer
    {
        public (string BillerId, string RunId, string AgentId, bool CanWrite)? Issued { get; private set; }

        public string Issue(string billerId, string runId, string agentId, bool canWrite)
        {
            Issued = (billerId, runId, agentId, canWrite);
            return "scoped-token";
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
