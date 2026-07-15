using Xunit;

namespace Pronto.BillerExperience.IntegrationTests;

public sealed class InfrastructureConfigurationTests
{
    [Fact]
    public void ProductionManifestsContainNoTemplatePlaceholders()
    {
        var root = FindRepositoryRoot();
        var overlay = Path.Combine(root, "deploy", "kubernetes", "overlays", "prod");

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
            var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", name));
            Assert.DoesNotContain("--admin", workflow, StringComparison.Ordinal);
            Assert.Contains("aadProfile.enableAzureRbac", workflow, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NonprodDeploymentRequiresExplicitApprovalOfThePrHead()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(
            Path.Combine(root, ".github", "workflows", "deploy-nonprod.yml"));

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
                     Path.Combine(root, "infra", "bicep", "main.bicep"),
                     Path.Combine(root, "infra", "bicep", "modules", "aiFoundry.bicep"),
                 })
        {
            var bicep = File.ReadAllText(path);
            Assert.Contains(
                "@allowed([\n  false\n])\nparam mcpConnectionEnabled bool = false",
                bicep,
                StringComparison.Ordinal);
        }

        var prodEnvironment = File.ReadAllText(
            Path.Combine(
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
    public void SecurityScansCannotSilentlyIgnoreHighSeverityFindings()
    {
        var root = FindRepositoryRoot();
        var workflows = Path.Combine(root, ".github", "workflows");
        Assert.DoesNotContain(
            "continue-on-error: true",
            File.ReadAllText(Path.Combine(workflows, "codeql.yml")),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "continue-on-error: true",
            File.ReadAllText(Path.Combine(workflows, "dependency-review.yml")),
            StringComparison.Ordinal);
        Assert.Contains(
            "--severity CRITICAL,HIGH --ignore-unfixed --exit-code 1",
            File.ReadAllText(Path.Combine(workflows, "scan.yml")),
            StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "deploy")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
