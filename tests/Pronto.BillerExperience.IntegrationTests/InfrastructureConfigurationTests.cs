using Xunit;

namespace Pronto.BillerExperience.IntegrationTests;

public sealed class InfrastructureConfigurationTests
{
    [Fact]
    public void ProductionManifestsContainNoTemplatePlaceholders()
    {
        var root = FindRepositoryRoot();
        var overlay = Path.Join(root, "deploy", "kubernetes", "overlays", "prod");

        Assert.All(
            Directory.GetFiles(overlay, "*.yaml", SearchOption.AllDirectories),
            path => Assert.DoesNotContain("${", File.ReadAllText(path), StringComparison.Ordinal));
    }

    [Fact]
    public void DeploymentWorkflowsRefuseAdministratorKubeconfig()
    {
        var root = FindRepositoryRoot();
        foreach (var name in new[] { "deploy-nonprod.yml", "deploy-prod.yml" })
        {
            var workflow = File.ReadAllText(Path.Join(root, ".github", "workflows", name));
            Assert.DoesNotContain("--admin", workflow, StringComparison.Ordinal);
            Assert.Contains("aadProfile.enableAzureRbac", workflow, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NonprodDeploymentRunsOnlyViaTrustedMergeQueue()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(
            Path.Join(root, ".github", "workflows", "deploy-nonprod.yml"));

        // The nonprod deploy runs with the repo's Azure secrets, so it must never be reachable
        // from the untrusted pull_request_target entry point. It is gated by the GitHub merge
        // queue (merge_group) — only maintainer-approved, enqueued commits deploy — and it
        // deploys exactly that queued commit (main + PR), not an arbitrary PR head.
        Assert.DoesNotContain("pull_request_target", workflow, StringComparison.Ordinal);
        Assert.Contains("merge_group:", workflow, StringComparison.Ordinal);
        Assert.Contains("github.event.merge_group.head_sha", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void DemoMcpRouteIsAuthenticatedAndBackedBySecretReferences()
    {
        var root = FindRepositoryRoot();
        foreach (var path in new[]
                 {
                     Path.Join(root, "infra", "bicep", "main.bicep"),
                     Path.Join(root, "infra", "bicep", "modules", "aiFoundry.bicep"),
                 })
        {
            var bicep = File.ReadAllText(path).ReplaceLineEndings("\n");
            Assert.Contains("@allowed([\n  true\n  false\n])", bicep, StringComparison.Ordinal);
        }

        var prodEnvironment = File.ReadAllText(
            Path.Join(
                root,
                "deploy",
                "kubernetes",
                "overlays",
                "prod",
                "biller-experience-api-env-patch.yaml"));
        Assert.Contains(
            "BillerExperience__Mcp__Enabled, value: \"true\"",
            prodEnvironment,
            StringComparison.Ordinal);
        Assert.Contains("BillerExperience__Mcp__ApiKey", prodEnvironment, StringComparison.Ordinal);
        Assert.Contains("BillerExperience__Mcp__CapabilitySigningKey", prodEnvironment, StringComparison.Ordinal);
        Assert.Contains("name: ic-agent-mcp", prodEnvironment, StringComparison.Ordinal);
        Assert.Contains("key: api-key", prodEnvironment, StringComparison.Ordinal);
        Assert.Contains("key: capability-signing-key", prodEnvironment, StringComparison.Ordinal);

        var routes = File.ReadAllText(
            Path.Join(root, "deploy", "kubernetes", "overlays", "prod", "httproutes.yaml"));
        Assert.Contains("value: /mcp", routes, StringComparison.Ordinal);
    }

    [Fact]
    public void SecurityScansAreBlockingOrExplicitlyAdvisory()
    {
        var root = FindRepositoryRoot();
        var workflows = Path.Join(root, ".github", "workflows");
        Assert.DoesNotContain(
            "continue-on-error: true",
            File.ReadAllText(Path.Join(workflows, "codeql.yml")),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "continue-on-error: true",
            File.ReadAllText(Path.Join(workflows, "dependency-review.yml")),
            StringComparison.Ordinal);
        var trivy = File.ReadAllText(Path.Join(workflows, "scan.yml"));
        Assert.Contains("name: Advisory security scan", trivy, StringComparison.Ordinal);
        Assert.Contains("name: Advisory Trivy filesystem & config", trivy, StringComparison.Ordinal);
        Assert.Contains(
            "High/critical findings are reported but do not block merges",
            trivy,
            StringComparison.Ordinal);
        Assert.DoesNotContain("--exit-code 1", trivy, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Join(directory.FullName, "deploy")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
