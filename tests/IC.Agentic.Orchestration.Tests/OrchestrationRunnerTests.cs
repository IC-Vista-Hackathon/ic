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

    [Fact]
    public async Task ObservableStepPublishesSafeFailureDetails()
    {
        var sink = new CollectingSink();
        var step = new ObservableOrchestrationStep<string, string>(
            "research", "Research", "Searching",
            static (_, _, _) => ValueTask.FromException<string>(new TimeoutException("provider details")), sink);

        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await step.ExecuteAsync("request", OrchestrationContext.Create(billerId: "biller-1")));

        var failed = Assert.Single(sink.Events, item => item.Status == OrchestrationEventStatus.Failed);
        Assert.Equal("research_failed", failed.ErrorCode);
        Assert.True(failed.Retryable);
        Assert.DoesNotContain("provider details", failed.Summary, StringComparison.Ordinal);
        Assert.NotNull(failed.DurationMs);
    }

    [Fact]
    public async Task FailureSinkErrorDoesNotMaskAgentError()
    {
        var step = new ObservableOrchestrationStep<string, string>(
            "designer", "Designer", "Designing",
            static (_, _, _) => ValueTask.FromException<string>(new InvalidOperationException("root failure")),
            new FailureEventThrowingSink());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await step.ExecuteAsync("request", OrchestrationContext.Create()));

        Assert.Equal("root failure", exception.Message);
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

    private sealed class FailureEventThrowingSink : IOrchestrationEventSink
    {
        public ValueTask PublishAsync(OrchestrationEvent activity, CancellationToken cancellationToken = default) =>
            activity.Status == OrchestrationEventStatus.Failed
                ? ValueTask.FromException(new IOException("sink failure"))
                : ValueTask.CompletedTask;
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
