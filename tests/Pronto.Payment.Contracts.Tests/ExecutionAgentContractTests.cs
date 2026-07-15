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
            Path.Combine(root, "agents", "execution", "instructions.md"));
        using var tools = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "agents", "execution", "tools.json")));
        var payInvoice = tools.RootElement
            .GetProperty("tools")
            .EnumerateArray()
            .Single(tool =>
                tool.GetProperty("function").GetProperty("name").GetString() == "pay_invoice");

        foreach (var required in payInvoice
                     .GetProperty("function")
                     .GetProperty("parameters")
                     .GetProperty("required")
                     .EnumerateArray())
        {
            Assert.Contains(required.GetString()!, instructions, StringComparison.Ordinal);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "agents")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
