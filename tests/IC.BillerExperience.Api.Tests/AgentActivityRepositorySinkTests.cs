using IC.Agentic.Orchestration.Abstractions;
using IC.BillerExperience.Api.Domain;
using IC.BillerExperience.Api.Infrastructure;
using IC.BillerExperience.Api.Infrastructure.Persistence;
using IC.BillerExperience.Contracts.V1.Onboarding;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IC.BillerExperience.Api.Tests;

public sealed class AgentActivityRepositorySinkTests
{
    [Fact]
    public async Task ActivityIsAppendedWithMonotonicSequenceWithoutRewritingRun()
    {
        var repository = new InMemoryBillerExperienceRepository();
        var run = new OnboardingRunRecord(
            "run-1", "biller-1", "test", OnboardingSessionState.CollectingInformation,
            0, [], [], DateTimeOffset.UtcNow);
        var saved = await repository.SaveRunAsync(run, null, CancellationToken.None);
        var sink = new AgentActivityRepositorySink(
            repository, "biller-1", "run-1", NullLogger.Instance);

        await Task.WhenAll(
            sink.PublishAsync(Event("first", "research"), CancellationToken.None).AsTask(),
            sink.PublishAsync(Event("second", "design"), CancellationToken.None).AsTask());

        var events = await repository.GetAgentActivityAsync("biller-1", "run-1", CancellationToken.None);
        Assert.Equal([1, 2], events.Select(item => item.Sequence));
        Assert.Equal(2, events.Select(item => item.EventId).Distinct(StringComparer.Ordinal).Count());
        var unchangedRun = await repository.GetRunAsync("biller-1", "run-1", CancellationToken.None);
        Assert.Equal(saved.ETag, unchangedRun!.ETag);
        Assert.Null(unchangedRun.AgentActivity);
    }

    private static OrchestrationEvent Event(string id, string agent) => new(
        id, 100, "run-1", agent, agent, OrchestrationEventStatus.Running,
        "Working", DateTimeOffset.UtcNow);
}
