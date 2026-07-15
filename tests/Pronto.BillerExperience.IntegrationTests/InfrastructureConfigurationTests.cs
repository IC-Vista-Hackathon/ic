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
    public void NonprodDeploymentRequiresExplicitApprovalOfThePrHead()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(
            Path.Join(root, ".github", "workflows", "deploy-nonprod.yml"));

        Assert.Contains("pull_request_target:", workflow, StringComparison.Ordinal);
        Assert.Contains("types: [labeled]", workflow, StringComparison.Ordinal);
        Assert.Contains("github.event.label.name == 'safe-to-deploy'", workflow, StringComparison.Ordinal);
        Assert.Contains("source_ref: ${{ github.event.pull_request.head.sha }}", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void McpConnectionRemainsDisabledWhileGatewayIsHttpOnly()
    {
        var root = FindRepositoryRoot();
        foreach (var path in new[]
                 {
                     Path.Join(root, "infra", "bicep", "main.bicep"),
                     Path.Join(root, "infra", "bicep", "modules", "aiFoundry.bicep"),
                 })
        {
            var bicep = File.ReadAllText(path);
            Assert.Contains(
                "@allowed([\n  false\n])\nparam mcpConnectionEnabled bool = false",
                bicep,
                StringComparison.Ordinal);
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
            "BillerExperience__Mcp__Enabled, value: \"false\"",
            prodEnvironment,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "BillerExperience__Mcp__ApiKey",
            prodEnvironment,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "BillerExperience__Mcp__PublicEndpoint",
            prodEnvironment,
            StringComparison.Ordinal);
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
