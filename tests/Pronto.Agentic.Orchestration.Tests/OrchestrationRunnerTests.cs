using Pronto.Agentic.Orchestration.Abstractions;
using Pronto.Agentic.Orchestration.Execution;
using Xunit;

namespace Pronto.Agentic.Orchestration.Tests;

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

    [Fact]
    public async Task ObservableStepPublishesRunningAndCompletedEvents()
    {
        var sink = new CollectingSink();
        var step = new ObservableOrchestrationStep<string, string>(
            "designer", "Experience Designer", "Applying requested changes",
            static (input, _, _) => ValueTask.FromResult(input.ToUpperInvariant()), sink);

        var result = await step.ExecuteAsync("pay later", OrchestrationContext.Create());

        Assert.Equal("PAY LATER", result);
        Assert.Collection(sink.Events,
            item => Assert.Equal(OrchestrationEventStatus.Running, item.Status),
            item => Assert.Equal(OrchestrationEventStatus.Completed, item.Status));
    }

    private sealed class CollectingSink : IOrchestrationEventSink
    {
        public List<OrchestrationEvent> Events { get; } = [];
        public ValueTask PublishAsync(OrchestrationEvent activity, CancellationToken cancellationToken = default)
        {
            Events.Add(activity);
            return ValueTask.CompletedTask;
        }
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
