using Pronto.Agentic.Orchestration.Abstractions;
using Pronto.BillerExperience.Api.Domain;
using Pronto.BillerExperience.Api.Infrastructure;
using Pronto.BillerExperience.Api.Infrastructure.Persistence;
using Pronto.BillerExperience.Contracts.V1.Onboarding;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Pronto.BillerExperience.Api.Tests;

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

    [Fact]
    public async Task WarningsAreMappedOntoThePersistedActivityEvent()
    {
        var repository = new InMemoryBillerExperienceRepository();
        await repository.SaveRunAsync(
            new OnboardingRunRecord("run-1", "biller-1", "test", OnboardingSessionState.CollectingInformation,
                0, [], [], DateTimeOffset.UtcNow),
            null, CancellationToken.None);
        var sink = new AgentActivityRepositorySink(repository, "biller-1", "run-1", NullLogger.Instance);
        var completed = Event("done", "research") with
        {
            Status = OrchestrationEventStatus.Completed,
            Warnings = ["conflicting phone numbers found", "some values unverifiable"]
        };

        await sink.PublishAsync(completed, CancellationToken.None);

        var events = await repository.GetAgentActivityAsync("biller-1", "run-1", CancellationToken.None);
        var mapped = Assert.Single(events);
        Assert.Equal(["conflicting phone numbers found", "some values unverifiable"], mapped.Warnings);
    }

    private static OrchestrationEvent Event(string id, string agent) => new(
        id, 100, "run-1", agent, agent, OrchestrationEventStatus.Running,
        "Working", DateTimeOffset.UtcNow);
}
