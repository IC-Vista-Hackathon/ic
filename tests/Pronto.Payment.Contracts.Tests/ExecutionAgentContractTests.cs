using System.Text.Json;
using Xunit;

namespace Pronto.Payment.Contracts.Tests;

public sealed class ExecutionAgentContractTests
{
    [Fact]
    public void InstructionsCoverEveryRequiredPaymentToolArgument()
    {
        var root = FindRepositoryRoot();
        var instructions = File.ReadAllText(
            Path.Join(root, "agents", "execution", "instructions.md"));
        using var tools = JsonDocument.Parse(
            File.ReadAllText(Path.Join(root, "agents", "execution", "tools.json")));
        foreach (var tool in tools.RootElement.GetProperty("tools").EnumerateArray())
        {
            foreach (var required in tool
                         .GetProperty("function")
                         .GetProperty("parameters")
                         .GetProperty("required")
                         .EnumerateArray())
            {
                Assert.Contains(required.GetString()!, instructions, StringComparison.Ordinal);
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Join(directory.FullName, "agents")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
