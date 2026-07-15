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

    private sealed class RecordingGateway(bool attachMcp) : IFoundryAgentAdministrationGateway
    {
        public List<DesiredFoundryAgent> Created { get; } = [];
        public Task<IReadOnlyList<ExistingFoundryAgent>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExistingFoundryAgent>>(Created.Select(item => new ExistingFoundryAgent(item.Name, item.Fingerprint, attachMcp)).ToArray());
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
