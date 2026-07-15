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

    [Fact]
    public async Task ObservableStepCanPublishAResultSpecificCompletionState()
    {
        var sink = new CollectingSink();
        var step = new ObservableOrchestrationStep<string, string>(
            "research", "Research", "Searching",
            static (input, _, _) => ValueTask.FromResult(input),
            sink,
            completion: _ => (OrchestrationEventStatus.Skipped, "No eligible provider.", "research.not_configured"));

        await step.ExecuteAsync("request", OrchestrationContext.Create());

        var skipped = Assert.Single(sink.Events, item => item.Status == OrchestrationEventStatus.Skipped);
        Assert.Equal("research.not_configured", skipped.ErrorCode);
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

    [Theory]
    [InlineData(OrchestrationEventStatus.Running)]
    [InlineData(OrchestrationEventStatus.Completed)]
    public async Task ActivitySinkErrorDoesNotFailSuccessfulStep(OrchestrationEventStatus failingStatus)
    {
        var calls = 0;
        var step = new ObservableOrchestrationStep<string, string>(
            "designer",
            "Designer",
            "Designing",
            (input, _, _) =>
            {
                calls++;
                return ValueTask.FromResult(input.ToUpperInvariant());
            },
            new StatusThrowingSink(failingStatus));

        var result = await step.ExecuteAsync("pay later", OrchestrationContext.Create());

        Assert.Equal("PAY LATER", result);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ResilientExecutionRetriesTransientFailureWithinBudget()
    {
        var attempts = 0;
        var result = await ResilientExecution.ExecuteAsync(
            (_, _) => ++attempts < 3
                ? ValueTask.FromException<string>(new HttpRequestException("transient"))
                : ValueTask.FromResult("recovered"),
            new(MaxAttempts: 3, InitialBackoff: TimeSpan.Zero));
        Assert.Equal("recovered", result);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task FanOutPreservesPartialSuccess()
    {
        var results = await BoundedFanOut.ExecuteAsync<int, int>([1, 2, 3], 2,
            (input, _, _) => input == 2
                ? ValueTask.FromException<int>(new InvalidOperationException("failed"))
                : ValueTask.FromResult(input * 10));
        Assert.Equal(2, results.Count(item => item.Succeeded));
        Assert.Single(results, item => !item.Succeeded);
    }

    [Fact]
    public async Task CheckpointedExecutionReturnsCompletedOutputAfterRestart()
    {
        var store = new MemoryStateStore();
        var calls = 0;
        var first = await CheckpointedExecution.ExecuteAsync(store, "biller-1", "run-1", "test", 1,
            _ => ValueTask.FromResult(++calls));
        var resumed = await CheckpointedExecution.ExecuteAsync(store, "biller-1", "run-1", "test", 1,
            _ => ValueTask.FromResult(++calls));
        Assert.Equal(1, first);
        Assert.Equal(1, resumed);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CheckpointedExecutionResumesAnInterruptedRunningStep()
    {
        var store = new MemoryStateStore();
        await Assert.ThrowsAsync<IOException>(async () =>
            await CheckpointedExecution.ExecuteAsync<int>(store, "biller-1", "run-2", "test", 1,
                _ => ValueTask.FromException<int>(new IOException("pod stopped"))));

        var recovered = await CheckpointedExecution.ExecuteAsync(store, "biller-1", "run-2", "test", 1,
            _ => ValueTask.FromResult(42));
        Assert.Equal(42, recovered);
    }

    [Fact]
    public async Task CheckpointedExecutionKeepsOutputForEachStep()
    {
        var store = new MemoryStateStore();
        await CheckpointedExecution.ExecuteAsync(
            store,
            "biller-1",
            "run-3",
            "test",
            1,
            _ => ValueTask.FromResult(111));
        await CheckpointedExecution.ExecuteAsync(
            store,
            "biller-1",
            "run-3",
            "test",
            2,
            _ => ValueTask.FromResult("step-two"));

        var resumed = await CheckpointedExecution.ExecuteAsync(
            store,
            "biller-1",
            "run-3",
            "test",
            1,
            _ => ValueTask.FromResult(222));

        Assert.Equal(111, resumed);
    }

    [Fact]
    public async Task CheckpointedExecutionCanResumeNullOutput()
    {
        var store = new MemoryStateStore();
        var calls = 0;
        await CheckpointedExecution.ExecuteAsync<string?>(
            store,
            "biller-1",
            "run-4",
            "test",
            1,
            _ =>
            {
                calls++;
                return ValueTask.FromResult<string?>(null);
            });

        var resumed = await CheckpointedExecution.ExecuteAsync<string?>(
            store,
            "biller-1",
            "run-4",
            "test",
            1,
            _ =>
            {
                calls++;
                return ValueTask.FromResult<string?>("unexpected");
            });

        Assert.Null(resumed);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task AttemptTimeoutRetriesAndEventuallyCompletes()
    {
        var attempts = 0;
        var result = await ResilientExecution.ExecuteAsync(async (_, token) =>
        {
            attempts++;
            if (attempts == 1) await Task.Delay(TimeSpan.FromSeconds(5), token);
            return "ok";
        }, new(MaxAttempts: 2, AttemptTimeout: TimeSpan.FromMilliseconds(20), InitialBackoff: TimeSpan.Zero));
        Assert.Equal("ok", result);
        Assert.Equal(2, attempts);
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

    private sealed class StatusThrowingSink(OrchestrationEventStatus failingStatus) : IOrchestrationEventSink
    {
        public ValueTask PublishAsync(
            OrchestrationEvent activity,
            CancellationToken cancellationToken = default) =>
            activity.Status == failingStatus
                ? ValueTask.FromException(new IOException("sink failure"))
                : ValueTask.CompletedTask;
    }

    private sealed class MemoryStateStore : IOrchestrationStateStore
    {
        private readonly Dictionary<(string PartitionKey, string RunId, int Step), OrchestrationCheckpoint> checkpoints = [];

        public ValueTask<OrchestrationCheckpoint?> ReadAsync(
            string partitionKey,
            string runId,
            int stepNumber,
            CancellationToken cancellationToken = default)
        {
            checkpoints.TryGetValue((partitionKey, runId, stepNumber), out var checkpoint);
            return ValueTask.FromResult(checkpoint);
        }

        public ValueTask<OrchestrationCheckpoint> SaveAsync(OrchestrationCheckpoint value, string? expectedETag = null, CancellationToken cancellationToken = default)
        {
            var checkpoint = value with { ETag = Guid.NewGuid().ToString("N") };
            checkpoints[(value.PartitionKey, value.RunId, value.Step)] = checkpoint;
            return ValueTask.FromResult(checkpoint);
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
