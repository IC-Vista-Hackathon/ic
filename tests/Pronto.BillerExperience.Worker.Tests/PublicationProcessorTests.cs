using System.Diagnostics;
using Pronto.BillerExperience.Contracts.V1.Experiences;
using Pronto.BillerExperience.Worker;
using Pronto.BillerExperience.Worker.Artifacts;
using Pronto.BillerExperience.Worker.Building;
using Pronto.BillerExperience.Worker.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Pronto.BillerExperience.Worker.Tests;

public sealed class PublicationProcessorTests
{
    [Fact]
    public async Task ProcessingSpanLinksOriginatingTraceparentAsConsumerNotParent()
    {
        var origin = new Activity("origin");
        origin.SetIdFormat(ActivityIdFormat.W3C);
        origin.Start();
        var traceparent = $"00-{origin.TraceId}-{origin.SpanId}-01";
        origin.Stop();

        var started = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == PublicationTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = started.Add
        };
        ActivitySource.AddActivityListener(listener);

        var repository = new FakeRepository(traceparent);
        Activity.Current = null;

        await Processor(repository, new FakePublisher()).ProcessAsync(repository.Deployment, CancellationToken.None);

        var processing = Assert.Single(started, activity => activity.OperationName == "publication.process");
        Assert.Equal(ActivityKind.Consumer, processing.Kind);
        var link = Assert.Single(processing.Links);
        Assert.Equal(origin.TraceId, link.Context.TraceId);
        Assert.Equal(origin.SpanId, link.Context.SpanId);
        // Originating context is a link, not the parent: processing runs in its own root trace.
        Assert.Null(processing.Parent);
        Assert.NotEqual(origin.TraceId, processing.TraceId);
    }

    [Fact]
    public async Task ProcessingSpanHasNoLinkWhenTraceparentMissing()
    {
        var started = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == PublicationTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = started.Add
        };
        ActivitySource.AddActivityListener(listener);

        var repository = new FakeRepository(traceparent: null);

        await Processor(repository, new FakePublisher()).ProcessAsync(repository.Deployment, CancellationToken.None);

        var processing = Assert.Single(started, activity => activity.OperationName == "publication.process");
        Assert.Equal(ActivityKind.Consumer, processing.Kind);
        Assert.Empty(processing.Links);
    }

    [Fact]
    public async Task SuccessfulPublicationMarksDeploymentAndWorkflowReady()
    {
        var repository = new FakeRepository();
        var publisher = new FakePublisher();
        var builder = new FakeBundleBuilder();
        var processor = Processor(repository, publisher, builder);

        await processor.ProcessAsync(repository.Deployment, CancellationToken.None);

        Assert.True(publisher.WasCalled);
        Assert.Equal(PublicationStates.Ready, repository.Saved?.Status);
        Assert.Equal(new Uri("https://pay.example.test/pay/city/"), repository.Saved?.PublishedUrl);
        Assert.True(repository.WorkflowPublished);
    }

    [Fact]
    public async Task BundleIsBuiltBeforeActivePointerIsPublished()
    {
        var repository = new FakeRepository();
        var publisher = new FakePublisher();
        var builder = new FakeBundleBuilder();
        var processor = Processor(repository, publisher, builder);

        await processor.ProcessAsync(repository.Deployment, CancellationToken.None);

        Assert.Equal("config-1", builder.Request?.Revision);
        Assert.Equal("city", builder.Request?.Slug);
        // The site must be uploaded (build) before the config publisher flips active.json.
        Assert.True(builder.BuiltAt < publisher.PublishedAt);
    }

    [Fact]
    public async Task BundleBuildFailureIsPersistedAndBlocksPublish()
    {
        var repository = new FakeRepository();
        var publisher = new FakePublisher();
        var builder = new FakeBundleBuilder { Failure = new BundleBuildException("vite build failed") };
        var processor = Processor(repository, publisher, builder);

        await processor.ProcessAsync(repository.Deployment, CancellationToken.None);

        Assert.False(publisher.WasCalled);
        Assert.Equal(PublicationStates.Failed, repository.Saved?.Status);
        Assert.Equal("BUNDLE_BUILD_FAILED", repository.Saved?.FailureCode);
        Assert.False(repository.WorkflowPublished);
    }

    [Fact]
    public async Task DisabledBuilderFailsPublicationBeforeActivePointer()
    {
        var repository = new FakeRepository();
        var publisher = new FakePublisher();
        var processor = Processor(repository, publisher, new NoOpBundleBuilder());

        await processor.ProcessAsync(repository.Deployment, CancellationToken.None);

        Assert.False(publisher.WasCalled);
        Assert.Equal(PublicationStates.Failed, repository.Saved?.Status);
        Assert.Equal("INVALID_PUBLICATION", repository.Saved?.FailureCode);
    }

    [Fact]
    public async Task ArtifactFailureIsPersistedWithoutCrashingPollingLoop()
    {
        var repository = new FakeRepository();
        var publisher = new FakePublisher { Failure = new InvalidOperationException("invalid artifact") };
        var processor = Processor(repository, publisher, new FakeBundleBuilder());

        await processor.ProcessAsync(repository.Deployment, CancellationToken.None);

        Assert.Equal(PublicationStates.Failed, repository.Saved?.Status);
        Assert.Equal("INVALID_PUBLICATION", repository.Saved?.FailureCode);
        Assert.False(repository.WorkflowPublished);
    }

    [Fact]
    public async Task CancellationReleasesClaimForImmediateRetry()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var repository = new FakeRepository();
        var processor = Processor(repository, new FakePublisher(), new FakeBundleBuilder());

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await processor.ProcessAsync(repository.Deployment, cancellation.Token));

        Assert.Equal(PublicationStates.Requested, repository.Saved?.Status);
        Assert.Null(repository.Saved?.LeaseExpiresAt);
    }

    [Fact]
    public async Task ActivatedPublicationFinalizationFailureDoesNotMarkLiveRevisionFailed()
    {
        var repository = new FakeRepository { WorkflowFailure = new InvalidOperationException("cosmos unavailable") };
        var publisher = new FakePublisher();
        var processor = Processor(repository, publisher, new FakeBundleBuilder());

        await processor.ProcessAsync(repository.Deployment, CancellationToken.None);

        Assert.True(publisher.WasCalled);
        Assert.Contains(repository.SavedRecords, deployment => deployment.Status == PublicationStates.Verifying);
        Assert.DoesNotContain(repository.SavedRecords, deployment => deployment.Status == PublicationStates.Failed);
    }

    [Fact]
    public async Task ReclaimedVerifyingPublicationDoesNotRebuildImmutableSite()
    {
        var repository = new FakeRepository(status: PublicationStates.Verifying);
        var publisher = new FakePublisher();
        var builder = new FakeBundleBuilder();

        await Processor(repository, publisher, builder).ProcessAsync(repository.Deployment, CancellationToken.None);

        Assert.True(publisher.WasCalled);
        Assert.Null(builder.Request);
        Assert.Equal(PublicationStates.Ready, repository.Saved?.Status);
    }

    [Fact]
    public async Task UncertainActiveWriteIsVerifiedByIdempotentRepublish()
    {
        var repository = new FakeRepository();
        var publisher = new UncertainThenSuccessfulPublisher();

        await Processor(repository, publisher, new FakeBundleBuilder())
            .ProcessAsync(repository.Deployment, CancellationToken.None);

        Assert.Equal(2, publisher.Calls);
        Assert.Equal(PublicationStates.Ready, repository.Saved?.Status);
        Assert.DoesNotContain(repository.SavedRecords, deployment => deployment.Status == PublicationStates.Failed);
    }

    private static PublicationProcessor Processor(FakeRepository repository, FakePublisher publisher) =>
        Processor(repository, publisher, new FakeBundleBuilder());

    private static PublicationProcessor Processor(
        FakeRepository repository,
        IExperienceArtifactPublisher publisher,
        IExperienceBundleBuilder bundleBuilder)
    {
        var options = Options.Create(new PublicationOptions
        {
            PublicBaseUrl = "https://pay.example.test",
            StorageEndpoint = "https://blob.example.test/",
            ContainerName = "payer-experiences",
        });
        return new PublicationProcessor(
            repository,
            new PublicationArtifactPlanFactory(options),
            publisher,
            bundleBuilder,
            options,
            NullLogger<PublicationProcessor>.Instance);
    }

    private sealed class FakePublisher : IExperienceArtifactPublisher
    {
        public Exception? Failure { get; init; }
        public bool WasCalled { get; private set; }
        public long PublishedAt { get; private set; }

        public ValueTask PublishAsync(PublicationArtifactPlan plan, CancellationToken cancellationToken)
        {
            WasCalled = true;
            PublishedAt = Stopwatch.GetTimestamp();
            return Failure is null ? ValueTask.CompletedTask : ValueTask.FromException(Failure);
        }
    }

    private sealed class FakeBundleBuilder : IExperienceBundleBuilder
    {
        public Exception? Failure { get; init; }
        public bool Enabled => true;
        public BundleBuildRequest? Request { get; private set; }
        public long BuiltAt { get; private set; }

        public ValueTask BuildAsync(BundleBuildRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            BuiltAt = Stopwatch.GetTimestamp();
            cancellationToken.ThrowIfCancellationRequested();
            return Failure is null ? ValueTask.CompletedTask : ValueTask.FromException(Failure);
        }
    }

    private sealed class UncertainThenSuccessfulPublisher : IExperienceArtifactPublisher
    {
        public int Calls { get; private set; }

        public ValueTask PublishAsync(PublicationArtifactPlan plan, CancellationToken cancellationToken)
        {
            Calls++;
            return Calls == 1
                ? ValueTask.FromException(new ArtifactActivationException(
                    "response lost after active write",
                    new IOException("connection reset")))
                : ValueTask.CompletedTask;
        }
    }

    private sealed class FakeRepository(string? traceparent = null, string status = PublicationStates.Applying) : IPublicationRepository
    {
        public PublicationDeployment Deployment { get; } = new(
            "deployment-1", "biller-1", 1, status, DateTimeOffset.UtcNow,
            Traceparent: traceparent, ETag: "etag");
        public PublicationDeployment? Saved { get; private set; }
        public List<PublicationDeployment> SavedRecords { get; } = [];
        public bool? WorkflowPublished { get; private set; }
        public Exception? WorkflowFailure { get; init; }

        public ValueTask<PublicationDeployment?> ClaimNextAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<PublicationDeployment?>(Deployment);

        public ValueTask<PublicationBiller> GetBillerAsync(string billerId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PublicationBiller(billerId, "City", "city"));

        public ValueTask<PublicationExperience> GetExperienceAsync(string billerId, int version, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PublicationExperience($"config-{version}", billerId, version, Definition(billerId)));

        public ValueTask<PublicationDeployment> SaveAsync(PublicationDeployment deployment, CancellationToken cancellationToken)
        {
            Saved = deployment;
            SavedRecords.Add(deployment);
            return ValueTask.FromResult(deployment);
        }

        public ValueTask MarkWorkflowAsync(string billerId, int version, bool published, CancellationToken cancellationToken)
        {
            if (WorkflowFailure is not null)
            {
                return ValueTask.FromException(WorkflowFailure);
            }

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
