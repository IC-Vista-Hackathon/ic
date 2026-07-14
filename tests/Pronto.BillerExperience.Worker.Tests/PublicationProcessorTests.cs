using Pronto.BillerExperience.Contracts.V1.Experiences;
using Pronto.BillerExperience.Worker;
using Pronto.BillerExperience.Worker.Artifacts;
using Pronto.BillerExperience.Worker.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Pronto.BillerExperience.Worker.Tests;

public sealed class PublicationProcessorTests
{
    [Fact]
    public async Task SuccessfulPublicationMarksDeploymentAndWorkflowReady()
    {
        var repository = new FakeRepository();
        var publisher = new FakePublisher();
        var processor = Processor(repository, publisher);

        await processor.ProcessAsync(repository.Deployment, CancellationToken.None);

        Assert.True(publisher.WasCalled);
        Assert.Equal(PublicationStates.Ready, repository.Saved?.Status);
        Assert.Equal(new Uri("https://pay.example.test/pay/city/"), repository.Saved?.PublishedUrl);
        Assert.True(repository.WorkflowPublished);
    }

    [Fact]
    public async Task ArtifactFailureIsPersistedWithoutCrashingPollingLoop()
    {
        var repository = new FakeRepository();
        var publisher = new FakePublisher { Failure = new InvalidOperationException("invalid artifact") };
        var processor = Processor(repository, publisher);

        await processor.ProcessAsync(repository.Deployment, CancellationToken.None);

        Assert.Equal(PublicationStates.Failed, repository.Saved?.Status);
        Assert.Equal("INVALID_PUBLICATION", repository.Saved?.FailureCode);
        Assert.False(repository.WorkflowPublished);
    }

    private static PublicationProcessor Processor(FakeRepository repository, FakePublisher publisher) => new(
        repository,
        new PublicationArtifactPlanFactory(Options.Create(new PublicationOptions { PublicBaseUrl = "https://pay.example.test" })),
        publisher,
        NullLogger<PublicationProcessor>.Instance);

    private sealed class FakePublisher : IExperienceArtifactPublisher
    {
        public Exception? Failure { get; init; }
        public bool WasCalled { get; private set; }

        public ValueTask PublishAsync(PublicationArtifactPlan plan, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Failure is null ? ValueTask.CompletedTask : ValueTask.FromException(Failure);
        }
    }

    private sealed class FakeRepository : IPublicationRepository
    {
        public PublicationDeployment Deployment { get; } = new(
            "deployment-1", "biller-1", 1, PublicationStates.Applying, DateTimeOffset.UtcNow, ETag: "etag");
        public PublicationDeployment? Saved { get; private set; }
        public bool? WorkflowPublished { get; private set; }

        public ValueTask<PublicationDeployment?> ClaimNextAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<PublicationDeployment?>(Deployment);

        public ValueTask<PublicationBiller> GetBillerAsync(string billerId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PublicationBiller(billerId, "City", "city"));

        public ValueTask<PublicationExperience> GetExperienceAsync(string billerId, int version, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PublicationExperience($"config-{version}", billerId, version, Definition(billerId)));

        public ValueTask<PublicationDeployment> SaveAsync(PublicationDeployment deployment, CancellationToken cancellationToken)
        {
            Saved = deployment;
            return ValueTask.FromResult(deployment);
        }

        public ValueTask MarkWorkflowAsync(string billerId, int version, bool published, CancellationToken cancellationToken)
        {
            WorkflowPublished = published;
            return ValueTask.CompletedTask;
        }

        private static BillerExperienceDefinition Definition(string billerId) => new(
            "1.0", billerId,
            new ExperienceBrand("City", "#085368", "#18B4E9", null, "Inter"),
            new ExperienceContent("Pay", "Welcome", "Support", new Uri("https://example.test/privacy"), new Uri("https://example.test/terms")),
            new PwaConfiguration("City Pay", "City", "#085368", "#FFFFFF", null), ["card"]);
    }
}
