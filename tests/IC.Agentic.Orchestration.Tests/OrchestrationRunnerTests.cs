using IC.Agentic.Orchestration.Abstractions;
using IC.Agentic.Orchestration.Execution;
using Xunit;

namespace IC.Agentic.Orchestration.Tests;

public sealed class OrchestrationRunnerTests
{
    [Fact]
    public async Task RunAsyncReturnsWorkflowOutput()
    {
        var runner = new OrchestrationRunner();
        var context = OrchestrationContext.Create(billerId: "biller-1");

        var result = await runner.RunAsync(new EchoWorkflow(), "hello", context);

        Assert.Equal("hello", result);
    }

    [Fact]
    public async Task RunAsyncPropagatesCancellation()
    {
        var runner = new OrchestrationRunner();
        var context = OrchestrationContext.Create();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await runner.RunAsync(new CancelledWorkflow(), "input", context, cancellation.Token));
    }

    private sealed class EchoWorkflow : IOrchestrationWorkflow<string, string>
    {
        public string Name => "echo";

        public ValueTask<string> ExecuteAsync(
            string input,
            OrchestrationContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(input);
    }

    private sealed class CancelledWorkflow : IOrchestrationWorkflow<string, string>
    {
        public string Name => "cancelled";

        public ValueTask<string> ExecuteAsync(
            string input,
            OrchestrationContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromCanceled<string>(cancellationToken);
    }
}
