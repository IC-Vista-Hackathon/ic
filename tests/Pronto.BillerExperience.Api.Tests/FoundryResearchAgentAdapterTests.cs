using Pronto.BillerExperience.Api.Configuration;
using Pronto.BillerExperience.Api.Infrastructure.Research;
using Pronto.BillerExperience.Contracts.V1.AgentContext;
using Pronto.BillerExperience.Contracts.V1.Research;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Pronto.BillerExperience.Api.Tests;

public sealed class FoundryResearchAgentAdapterTests
{
    [Fact]
    public async Task CatalogReturnsFullInventoryWithApprovalAndCapabilityMetadata()
    {
        var gateway = new StubGateway
        {
            Agents =
            [
                Agent("approved", ("ic.approved", "true"), ("ic.capabilities", "biller_research,brand")),
                Agent("not-approved", ("ic.approved", "false"), ("ic.capabilities", "biller_research")),
                Agent("untagged", ("ic.approved", "true"))
            ]
        };
        var adapter = Create(gateway);

        var agents = await adapter.ListAsync(CancellationToken.None);

        Assert.Equal(3, agents.Count);
        var agent = Assert.Single(agents, item => item.Id == "approved");
        Assert.Equal("approved", agent.Id);
        Assert.True(agent.Approved);
        Assert.Equal("foundry", agent.Provider);
        Assert.Contains("biller_research", agent.Capabilities);
        Assert.Contains("brand", agent.Capabilities);
        Assert.False(Assert.Single(agents, item => item.Id == "not-approved").Approved);
        Assert.Empty(Assert.Single(agents, item => item.Id == "untagged").Capabilities);
    }

    [Fact]
    public async Task DispatchMapsStructuredFactsAndSdkCitations()
    {
        var gateway = new StubGateway
        {
            Output = new FoundryAgentOutput(
                """{"facts":[{"name":"brand","value":"Example","sourceUrl":"https://example.com/about","confidence":0.91}],"sources":[{"url":"https://example.com/about","title":"About"}],"warnings":[]}""",
                [new FoundryCitation(new Uri("https://example.com/news"), "News")])
        };
        var adapter = Create(gateway);

        var response = await adapter.DispatchAsync(Descriptor(), Request(), null, CancellationToken.None);

        Assert.Equal(ResearchOutcome.Completed, response.Outcome);
        Assert.Single(response.Facts);
        Assert.Equal(2, response.Sources.Count);
        Assert.Equal("worker", gateway.InvokedAgentId);
        Assert.Contains("Every fact must cite", gateway.Prompt);
        Assert.Contains("Mandatory Responsible AI policy", gateway.Prompt);
        Assert.Contains("private chain-of-thought", gateway.Prompt);
    }

    [Fact]
    public async Task DispatchNormalizesBrandAliasesAndGivesBrandAgentTypedAssignment()
    {
        var gateway = new StubGateway
        {
            Output = new FoundryAgentOutput(
                """{"facts":[{"name":"primaryColor","value":"#123456","sourceUrl":"https://example.com","confidence":0.9}],"sources":[],"warnings":[]}""",
                [])
        };
        var adapter = Create(gateway);
        var descriptor = new ResearchAgentDescriptor(
            "biller-brand-research", "Brand Research", new HashSet<string> { "biller_research" });

        var response = await adapter.DispatchAsync(descriptor, Request(), null, CancellationToken.None);

        Assert.Equal(BrandEvidenceFacts.PrimaryColor, Assert.Single(response.Facts).Name);
        Assert.Contains("Find official brand identity evidence", gateway.Prompt);
        Assert.Contains("brand_primary_color", gateway.Prompt);
    }

    [Fact]
    public async Task CompositeCatalogAlwaysPlacesDeterministicSameSiteWorkerFirst()
    {
        var gateway = new StubGateway
        {
            Agents = [Agent("foundry-worker", ("ic.approved", "true"), ("ic.capabilities", "biller_research"))]
        };
        var catalog = new CompositeResearchAgentCatalog(Create(gateway));

        var agents = await catalog.ListAsync(CancellationToken.None);

        Assert.Equal("same-site-research", agents[0].Id);
        Assert.Equal("local", agents[0].Provider);
        Assert.Contains(agents, agent => agent.Id == "foundry-worker" && agent.Provider == "foundry");
    }

    [Fact]
    public async Task DispatchSkipsValidEmptyEvidenceWithoutAcceptingUncitedFacts()
    {
        var gateway = new StubGateway
        {
            Output = new FoundryAgentOutput("""{"facts":[],"sources":[],"warnings":[]}""", [])
        };
        var adapter = Create(gateway);

        var response = await adapter.DispatchAsync(Descriptor(), Request(), null, CancellationToken.None);

        Assert.Equal(ResearchOutcome.Skipped, response.Outcome);
        Assert.Equal("research.no_cited_facts", response.ErrorCode);
        Assert.False(response.Retryable);
        Assert.Empty(response.Facts);
    }

    [Fact]
    public async Task DispatchFailsClosedWhenCandidateFactsHaveInvalidCitations()
    {
        var gateway = new StubGateway
        {
            Output = new FoundryAgentOutput(
                """{"facts":[{"name":"brand","value":"Unsupported","sourceUrl":"not-a-url","confidence":0.9}],"sources":[],"warnings":[]}""",
                [])
        };
        var adapter = Create(gateway);

        var response = await adapter.DispatchAsync(Descriptor(), Request(), null, CancellationToken.None);

        Assert.Equal(ResearchOutcome.Failed, response.Outcome);
        Assert.Equal("research.foundry_invalid_output", response.ErrorCode);
        Assert.Empty(response.Facts);
    }

    [Fact]
    public async Task DispatchCanResearchFromBillerIdentityWhenWebsiteIsMissing()
    {
        var gateway = new StubGateway
        {
            Output = new FoundryAgentOutput(
                """{"facts":[{"name":"brand","value":"Example","sourceUrl":"https://example.com","confidence":0.9}],"sources":[{"url":"https://example.com","title":"Home"}],"warnings":[]}""",
                [])
        };
        var adapter = Create(gateway);

        var response = await adapter.DispatchAsync(
            Descriptor(),
            new BillerResearchRequest(null, "Research brand", BillerName: "Example Water", BillType: "Utility", PostalCode: "02110"),
            null,
            CancellationToken.None);

        Assert.Equal(ResearchOutcome.Completed, response.Outcome);
        Assert.Contains("Biller name: Example Water", gateway.Prompt);
        Assert.Contains("Website: not supplied", gateway.Prompt);
    }

    [Fact]
    public async Task DispatchReceivesSanitizedSharedContextWithoutCredentials()
    {
        var gateway = new StubGateway
        {
            Output = new FoundryAgentOutput(
                """{"facts":[{"name":"brand","value":"Example","sourceUrl":"https://example.com","confidence":0.9}],"sources":[{"url":"https://example.com","title":"Home"}],"warnings":[]}""",
                [])
        };
        var adapter = Create(gateway);

        await adapter.DispatchAsync(
            Descriptor(),
            Request(),
            new ResearchAgentInvocationContext(new AgentContextSnapshot(
                "biller-1",
                "run-1",
                3,
                "Build a branded payment experience",
                [new AgentContextEntry(
                    "entry-1",
                    AgentContextEntryKind.Correction,
                    "onboarding",
                    "brand",
                    "Use navy blue.",
                    [],
                    false,
                    DateTimeOffset.UtcNow)],
                DateTimeOffset.UtcNow)),
            CancellationToken.None);

        Assert.Contains("orchestration read the shared context through MCP", gateway.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Use navy blue.", gateway.Prompt);
        Assert.Contains("Do not call shared-context MCP tools yourself", gateway.Prompt);
        Assert.DoesNotContain("scoped-token", gateway.Prompt);
        Assert.DoesNotContain("X-IC-MCP-Key", gateway.Prompt);
        Assert.Contains("credentials", gateway.Prompt);
    }

    [Fact]
    public async Task ConsolidatorInvokesConfiguredCoordinatorAgent()
    {
        var gateway = new StubGateway
        {
            Output = new FoundryAgentOutput(
                """{"facts":[{"name":"brand","value":"Example","sourceUrl":"https://example.com","confidence":0.9}],"sources":[{"url":"https://example.com","title":"Home"}],"warnings":[]}""",
                [])
        };
        var adapter = Create(gateway, "coordinator");

        var response = await adapter.ConsolidateAsync(Request(), [Completed()], null, CancellationToken.None);

        Assert.Equal(ResearchOutcome.Completed, response.Outcome);
        Assert.Equal("coordinator", gateway.InvokedAgentId);
        Assert.Contains("Candidate results", gateway.Prompt);
    }

    private static FoundryResearchAgentAdapter Create(StubGateway gateway, string coordinator = "") => new(
        gateway,
        Options.Create(new BillerExperienceOptions
        {
            Research = new ResearchOptions { CoordinatorAgentId = coordinator }
        }),
        NullLogger<FoundryResearchAgentAdapter>.Instance);

    private static FoundryAgentDefinition Agent(string id, params (string Key, string Value)[] metadata) =>
        new(id, id, metadata.ToDictionary(pair => pair.Key, pair => pair.Value));

    private static ResearchAgentDescriptor Descriptor() =>
        new("worker", "Worker", new HashSet<string> { "biller_research" });

    private static BillerResearchRequest Request() =>
        new(new Uri("https://example.com"), "Research brand and payment context");

    private static BillerResearchResponse Completed() => new(
        ResearchOutcome.Completed,
        [new ResearchFact("brand", "Example", new Uri("https://example.com"), 0.9)],
        [new ResearchSource(new Uri("https://example.com"), "Home", DateTimeOffset.UtcNow)],
        []);

    private sealed class StubGateway : IFoundryAgentServiceGateway
    {
        public IReadOnlyList<FoundryAgentDefinition> Agents { get; init; } = [];
        public FoundryAgentOutput Output { get; init; } = new("{}", []);
        public string? InvokedAgentId { get; private set; }
        public string? Prompt { get; private set; }

        public Task<IReadOnlyList<FoundryAgentDefinition>> ListAgentsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Agents);

        public Task<FoundryAgentOutput> InvokeAsync(string agentId, string prompt, CancellationToken cancellationToken)
        {
            InvokedAgentId = agentId;
            Prompt = prompt;
            return Task.FromResult(Output);
        }
    }
}
