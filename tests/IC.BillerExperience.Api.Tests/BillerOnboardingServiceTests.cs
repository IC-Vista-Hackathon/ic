using System.Diagnostics;
using IC.BillerExperience.Api.Application;
using IC.BillerExperience.Api.Infrastructure;
using IC.BillerExperience.Api.Infrastructure.AI;
using IC.BillerExperience.Api.Infrastructure.Persistence;
using IC.BillerExperience.Contracts.V1.Billers;
using IC.BillerExperience.Contracts.V1.Deployments;
using IC.BillerExperience.Contracts.V1.Experiences;
using IC.BillerExperience.Contracts.V1.Onboarding;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IC.BillerExperience.Api.Tests;

public sealed class BillerOnboardingServiceTests
{
    [Fact]
    public async Task CompleteWorkflowProducesPublicationRequestAndTelemetry()
    {
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == BillerExperienceTelemetry.SourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add
        };
        ActivitySource.AddActivityListener(listener);
        var service = CreateService();

        var created = await service.CreateAsync(CreateRequest(), CancellationToken.None);
        var chat = await service.SendMessageAsync(
            created.Biller.BillerId,
            new SendOnboardingMessageRequest("Use #174A5B and keep the language concise."),
            CancellationToken.None);
        var approved = await service.ApproveAsync(
            created.Biller.BillerId,
            new ApproveExperienceRequest(chat.Draft!.Revision, "test-user"),
            CancellationToken.None);
        var deployment = await service.PublishAsync(
            created.Biller.BillerId,
            new PublishExperienceRequest(created.Biller.BillerId, approved.Revision),
            CancellationToken.None);

        Assert.Equal(OnboardingSessionState.DraftReady, chat.Session.State);
        Assert.Equal("#174A5B", chat.Draft.Definition.Brand.PrimaryColor);
        Assert.Equal(ExperienceRevisionState.Approved, approved.State);
        Assert.Equal(DeploymentState.Requested, deployment.State);
        Assert.Contains(activities, activity => activity.OperationName == "onboarding.chat");
        Assert.Contains(activities, activity => activity.OperationName == "experience.approve");
    }

    [Fact]
    public async Task InvalidSlugFailsBeforePersistence()
    {
        var service = CreateService();
        var request = CreateRequest() with { Slug = "Not Valid" };

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.CreateAsync(request, CancellationToken.None));

        Assert.Contains("Slug", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DuplicatePublishIsIdempotent()
    {
        var service = CreateService();
        var created = await service.CreateAsync(CreateRequest(), CancellationToken.None);
        var chat = await service.SendMessageAsync(created.Biller.BillerId, new("Ready for review"), CancellationToken.None);
        var approved = await service.ApproveAsync(created.Biller.BillerId, new(chat.Draft!.Revision, "test-user"), CancellationToken.None);
        var request = new PublishExperienceRequest(created.Biller.BillerId, approved.Revision);

        var first = await service.PublishAsync(created.Biller.BillerId, request, CancellationToken.None);
        var second = await service.PublishAsync(created.Biller.BillerId, request, CancellationToken.None);

        Assert.Equal(first.DeploymentId, second.DeploymentId);
    }

    private static BillerOnboardingService CreateService()
    {
        var repository = new InMemoryBillerExperienceRepository();
        var generator = new DeterministicExperienceDraftGenerator(NullLogger<DeterministicExperienceDraftGenerator>.Instance);
        return new(repository, generator, NullLogger<BillerOnboardingService>.Instance);
    }

    private static CreateBillerRequest CreateRequest() =>
        new("City of Vista", "city-of-vista", "Utility", "02110", new Uri("https://vista.example"));
}
