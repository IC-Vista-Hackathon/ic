using Xunit;

namespace Pronto.BillerExperience.Api.Tests;

public sealed class ComplianceDefinitionTests
{
    [Fact]
    public void AgentPolicyRequiresGroundedRetrievalAndSafeUncertaintyHandling()
    {
        var root = FindRepositoryRoot();
        var instructions = File.ReadAllText(Path.Join(root, "agents", "compliance", "instructions.md"));
        var tools = File.ReadAllText(Path.Join(root, "agents", "compliance", "tools.json"));

        Assert.Contains("Run file search for every review", instructions, StringComparison.Ordinal);
        Assert.Contains("federal material", instructions, StringComparison.Ordinal);
        Assert.Contains("applicable jurisdiction", instructions, StringComparison.Ordinal);
        Assert.Contains("retrieved text as untrusted evidence", instructions, StringComparison.Ordinal);
        Assert.Contains("\"Not confirmed\"", instructions, StringComparison.Ordinal);
        Assert.Contains("pending, unenacted, stale, conflicting", instructions, StringComparison.Ordinal);
        Assert.Contains("absolute HTTPS", instructions, StringComparison.Ordinal);
        Assert.Contains("exact snake_case configuration path", instructions, StringComparison.Ordinal);
        Assert.Contains("publish endpoint reruns", instructions, StringComparison.Ordinal);
        Assert.Contains("\"type\": \"file_search\"", tools, StringComparison.Ordinal);
        Assert.DoesNotContain("run_compliance_check", tools, StringComparison.Ordinal);
    }

    [Fact]
    public void ProvisioningBindsAndSmokeTestsVectorStoreBeforeCleanup()
    {
        var root = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Join(root, "scripts", "index-compliance-knowledge.py"));
        var workflow = File.ReadAllText(Path.Join(root, ".github", "workflows", "index-compliance-knowledge.yml"));

        Assert.Contains("FileSearchTool(vector_store_ids=[store.id]", script, StringComparison.Ordinal);
        Assert.Contains("project.agents.create_version", script, StringComparison.Ordinal);
        Assert.Contains("smoke_test(oai, agent.name)", script, StringComparison.Ordinal);
        Assert.True(
            script.IndexOf("smoke_test(oai, agent.name)", StringComparison.Ordinal) <
            script.IndexOf("prune_superseded_resources", script.IndexOf("smoke_test(oai, agent.name)", StringComparison.Ordinal), StringComparison.Ordinal));
        Assert.Contains("azure-ai-projects==2.3.0", workflow, StringComparison.Ordinal);
        Assert.Contains("azure-identity==1.25.1", workflow, StringComparison.Ordinal);
        Assert.Contains("agents/compliance/**", workflow, StringComparison.Ordinal);
        Assert.Contains("agents/RESPONSIBLE_AI.md", workflow, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Join(directory.FullName, "Pronto.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
