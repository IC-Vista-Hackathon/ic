using Xunit;

namespace IC.BillerExperience.Api.Tests;

public sealed class ResponsibleAiPolicyTests
{
    [Fact]
    public void EveryAgentDefinitionReferencesMandatoryResponsibleAiPolicy()
    {
        var root = FindRepositoryRoot();
        var agentRoot = Path.Combine(root, "agents");
        var instructionFiles = Directory.GetFiles(agentRoot, "instructions.md", SearchOption.AllDirectories);

        Assert.NotEmpty(instructionFiles);
        foreach (var file in instructionFiles)
        {
            var instructions = File.ReadAllText(file);
            Assert.Contains("RESPONSIBLE_AI.md", instructions, StringComparison.Ordinal);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IC.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not find the repository root from the test output directory.");
    }
}
