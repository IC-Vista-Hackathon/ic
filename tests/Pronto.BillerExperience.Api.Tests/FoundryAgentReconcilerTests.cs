using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pronto.BillerExperience.Api.Configuration;
using Pronto.BillerExperience.Api.Infrastructure.Research;
using Xunit;

namespace Pronto.BillerExperience.Api.Tests;

public sealed class FoundryAgentReconcilerTests
{
    [Fact]
    public async Task ReconcilesDriftAndRequiresVerifiedMcpAttachment()
    {
        var root = FindRepositoryRoot();
        var configuration = new BillerExperienceOptions
        {
            AgentProvisioning = new AgentProvisioningOptions { Enabled = true, DefinitionsPath = "agents" }
        };
        var gateway = new RecordingGateway(attachMcp: true);
        var reconciler = new FoundryAgentReconciler(gateway, Options.Create(configuration), new TestEnvironment(root), NullLogger<FoundryAgentReconciler>.Instance);

        await reconciler.ReconcileAsync(CancellationToken.None);

        Assert.Equal(3, gateway.Created.Count(item => item.Capability == "biller_research"));
        Assert.Contains(gateway.Created, item => item.Name == "biller-research" && item.Capability == "biller_research");
        Assert.Contains(gateway.Created, item => item.Name == "biller-brand-research" && item.Capability == "biller_research");
        Assert.Contains(gateway.Created, item => item.Name == "biller-payment-policy-research" && item.Capability == "biller_research");
        Assert.All(gateway.Created, item => Assert.Equal(64, item.Fingerprint.Length));
    }

    [Fact]
    public async Task ReconciliationFailsWhenCreatedVersionDoesNotExposeMcp()
    {
        var root = FindRepositoryRoot();
        var configuration = new BillerExperienceOptions
        {
            AgentProvisioning = new AgentProvisioningOptions { Enabled = true, DefinitionsPath = "agents" }
        };
        var reconciler = new FoundryAgentReconciler(new RecordingGateway(false), Options.Create(configuration), new TestEnvironment(root), NullLogger<FoundryAgentReconciler>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => reconciler.ReconcileAsync(CancellationToken.None));
    }

    [Fact]
    public void PerAgentAllowedToolsExposeDeclaredRouterToolsPlusSharedContext()
    {
        var desired = FoundryAgentReconciler.LoadDesired(
            new AgentProvisioningOptions { DefinitionsPath = "agents" },
            FindRepositoryRoot());

        // Every provisioned agent keeps the shared-context tools.
        Assert.All(desired, agent =>
        {
            Assert.Contains("get_goal_context", agent.AllowedTools);
            Assert.Contains("append_context", agent.AllowedTools);
        });

        // Payer-side agents additionally receive exactly the router tools they declare.
        var billIntelligence = Assert.Single(desired, item => item.Name == "bill-intelligence");
        Assert.Equal(
            ["get_goal_context", "append_context", "list_invoices", "get_invoice", "get_payment_quote"],
            billIntelligence.AllowedTools);

        var policy = Assert.Single(desired, item => item.Name == "policy");
        Assert.Equal(
            ["get_goal_context", "append_context", "verify_payer_account", "get_payer_profile", "update_payer_preferences", "register_payer"],
            policy.AllowedTools);

        var execution = Assert.Single(desired, item => item.Name == "execution");
        Assert.Equal(
            ["get_goal_context", "append_context", "bind_execution_capability", "create_payment_intent", "submit_payment"],
            execution.AllowedTools);

        // A tool-less reasoning stage gets only the shared-context tools.
        var planning = Assert.Single(desired, item => item.Name == "financial-planning");
        Assert.Equal(["get_goal_context", "append_context"], planning.AllowedTools);

        // Non-MCP declarations (update_config, research_website) are never granted as MCP tools.
        var research = Assert.Single(desired, item => item.Name == "biller-research");
        Assert.Equal(["get_goal_context", "append_context"], research.AllowedTools);
    }

    [Fact]
    public async Task ReconcilesAgentWhoseCapabilityMetadataDrifted()
    {
        var root = FindRepositoryRoot();
        var options = new AgentProvisioningOptions { Enabled = true, DefinitionsPath = "agents" };
        var desired = Assert.Single(FoundryAgentReconciler.LoadDesired(options, root), item => item.Name == "biller-payment-policy-research");
        var gateway = new RecordingGateway(true,
            new ExistingFoundryAgent(desired.Name, desired.Fingerprint, true, "biller_payment_policy_research"));
        var configuration = new BillerExperienceOptions { AgentProvisioning = options };
        var reconciler = new FoundryAgentReconciler(gateway, Options.Create(configuration), new TestEnvironment(root), NullLogger<FoundryAgentReconciler>.Instance);

        await reconciler.ReconcileAsync(CancellationToken.None);

        Assert.Contains(gateway.Created, item => item.Name == desired.Name && item.Capability == "biller_research");
    }

    [Fact]
    public void BillerResearchDefinitionMatchesProvisionedToolsAndWireContract()
    {
        var desired = FoundryAgentReconciler.LoadDesired(
            new AgentProvisioningOptions { DefinitionsPath = "agents" },
            FindRepositoryRoot());

        var research = Assert.Single(desired, item => item.Name == "biller-research");
        Assert.DoesNotContain(desired, item => item.Name == "compliance");
        Assert.Contains("built-in web-search tool", research.Instructions);
        Assert.Contains("orchestration reads shared context through MCP", research.Instructions);
        Assert.Contains("Do not request, reproduce, or pass capability tokens", research.Instructions);
        Assert.Contains("Return only one JSON object", research.Instructions);
        Assert.Contains("Do not call or claim to call `research_website`", research.Instructions);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "agents"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed class RecordingGateway(bool attachMcp, ExistingFoundryAgent? initial = null) : IFoundryAgentAdministrationGateway
    {
        public List<DesiredFoundryAgent> Created { get; } = [];
        public Task<IReadOnlyList<ExistingFoundryAgent>> ListAsync(CancellationToken cancellationToken)
        {
            var current = Created.Select(item => new ExistingFoundryAgent(item.Name, item.Fingerprint, attachMcp, item.Capability)).ToList();
            if (initial is not null && current.All(item => item.Name != initial.Name)) current.Add(initial);
            return Task.FromResult<IReadOnlyList<ExistingFoundryAgent>>(current);
        }
        public Task CreateVersionAsync(DesiredFoundryAgent agent, CancellationToken cancellationToken) { Created.Add(agent); return Task.CompletedTask; }
    }

    private sealed class TestEnvironment(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(root);
    }
}
