using System.Collections.Concurrent;
using IC.BillerExperience.Contracts.V1.Experiences;
using IC.BillerExperience.Worker.Artifacts;
using IC.BillerExperience.Worker.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace IC.BillerExperience.Worker.Tests;

/// <summary>Covers the polling/claim loop itself (PublicationProcessor is tested separately).</summary>
public sealed class PublicationWorkerTests
{
    [Fact]
    public async Task ClaimedDeploymentIsProcessedAndMarkedReady()
    {
        var repository = new QueueRepository();
        repository.Enqueue(QueueRepository.NewDeployment("deployment-1"));
        using var worker = Worker(repository);

        await worker.StartAsync(CancellationToken.None);
        await WaitForAsync(() => !repository.Saved.IsEmpty);
        await worker.StopAsync(CancellationToken.None);

        var saved = Assert.Single(repository.Saved, d => d.Status == PublicationStates.Ready);
        Assert.Equal("deployment-1", saved.Id);
    }

    [Fact]
    public async Task PollErrorDoesNotKillTheLoop()
    {
        var repository = new QueueRepository { FailuresBeforeSuccess = 1 };
        repository.Enqueue(QueueRepository.NewDeployment("deployment-2"));
        using var worker = Worker(repository);

        await worker.StartAsync(CancellationToken.None);
        await WaitForAsync(() => !repository.Saved.IsEmpty, timeoutSeconds: 10);
        await worker.StopAsync(CancellationToken.None);

        Assert.Contains(repository.Saved, d => d.Status == PublicationStates.Ready);
    }

    [Fact]
    public async Task StopsPromptlyWhenIdle()
    {
        var repository = new QueueRepository();
        using var worker = Worker(repository);

        await worker.StartAsync(CancellationToken.None);
        var stop = worker.StopAsync(CancellationToken.None);
        var finished = await Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(10)));

        Assert.Same(stop, finished);
    }

    private static PublicationWorker Worker(QueueRepository repository)
    {
        var options = Options.Create(new PublicationOptions
        {
            PublicBaseUrl = "https://pay.example.test",
            PollIntervalSeconds = 1,
        });
        var processor = new PublicationProcessor(
            repository,
            new PublicationArtifactPlanFactory(options),
            new AlwaysSucceedsPublisher(),
            NullLogger<PublicationProcessor>.Instance);
        return new PublicationWorker(
            repository, processor, options, NullLogger<PublicationWorker>.Instance);
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutSeconds = 5)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (!condition() && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        Assert.True(condition(), "condition not reached before timeout");
    }

    private sealed class AlwaysSucceedsPublisher : IExperienceArtifactPublisher
    {
        public ValueTask PublishAsync(PublicationArtifactPlan plan, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
    }

    private sealed class QueueRepository : IPublicationRepository
    {
        private readonly ConcurrentQueue<PublicationDeployment> pending = new();
        private int failuresRemaining;

        public int FailuresBeforeSuccess
        {
            init => failuresRemaining = value;
        }

        public ConcurrentBag<PublicationDeployment> Saved { get; } = [];

        public static PublicationDeployment NewDeployment(string id) => new(
            id, "biller-1", 1, PublicationStates.Applying, DateTimeOffset.UtcNow, ETag: "etag");

        public void Enqueue(PublicationDeployment deployment) => pending.Enqueue(deployment);

        public ValueTask<PublicationDeployment?> ClaimNextAsync(CancellationToken cancellationToken)
        {
            if (failuresRemaining > 0)
            {
                failuresRemaining--;
                throw new InvalidOperationException("transient poll failure");
            }

            pending.TryDequeue(out var deployment);
            return ValueTask.FromResult(deployment);
        }

        public ValueTask<PublicationBiller> GetBillerAsync(string billerId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PublicationBiller(billerId, "City", "city"));

        public ValueTask<PublicationExperience> GetExperienceAsync(string billerId, int version, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PublicationExperience($"config-{version}", billerId, version, Definition(billerId)));

        public ValueTask<PublicationDeployment> SaveAsync(PublicationDeployment deployment, CancellationToken cancellationToken)
        {
            Saved.Add(deployment);
            return ValueTask.FromResult(deployment);
        }

        public ValueTask MarkWorkflowAsync(string billerId, int version, bool published, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        private static BillerExperienceDefinition Definition(string billerId) => new(
            "1.0", billerId,
            new ExperienceBrand("City", "#085368", "#18B4E9", null, "Inter"),
            new ExperienceContent("Pay", "Welcome", "Support", new Uri("https://example.test/privacy"), new Uri("https://example.test/terms")),
            new PwaConfiguration("City Pay", "City", "#085368", "#FFFFFF", null), ["card"]);
    }
}
