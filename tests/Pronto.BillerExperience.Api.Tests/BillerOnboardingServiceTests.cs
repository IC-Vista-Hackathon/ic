using System.Diagnostics;
using Pronto.BillerExperience.Api.Application;
using Pronto.BillerExperience.Api.Infrastructure;
using Pronto.BillerExperience.Api.Infrastructure.AI;
using Pronto.BillerExperience.Api.Infrastructure.Persistence;
using Pronto.BillerExperience.Contracts.V1.Billers;
using Pronto.BillerExperience.Contracts.V1.Deployments;
using Pronto.BillerExperience.Contracts.V1.Experiences;
using Pronto.BillerExperience.Contracts.V1.Onboarding;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Xunit;

namespace Pronto.BillerExperience.Api.Tests;

public sealed class BillerOnboardingServiceTests
{
    [Fact]
    public void CosmosRecordsUseRequiredWirePropertyNames()
    {
        var record = new Pronto.BillerExperience.Api.Domain.BillerRecord(
            "biller-1", "City", "city", "Utility", "02110", null, null, null, [], BillerStatus.Prospect, DateTimeOffset.UtcNow);

        var json = JsonConvert.SerializeObject(record);

        Assert.Contains("\"id\":\"biller-1\"", json, StringComparison.Ordinal);
        Assert.Contains("\"postal_code\":\"02110\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Id\"", json, StringComparison.Ordinal);
    }

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
            new SendOnboardingMessageRequest("Use #174A5B, keep the language concise, and change the primary action to Pay later."),
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
        Assert.Equal(ExperienceActionType.SchedulePayment, chat.Draft.Definition.Ui!.Actions.Single().Action);
        Assert.Equal("Pay Later", chat.Draft.Definition.Ui.Actions.Single().Label);
        Assert.NotNull(chat.Draft.Definition.Preferences);
        Assert.Equal(["card", "ach"], chat.Draft.Definition.Preferences.AcceptedMethods);
        Assert.True(chat.Draft.Definition.Preferences.OfferAutopay);
        Assert.Equal(ExperienceRevisionState.Approved, approved.State);
        Assert.Equal(DeploymentState.Requested, deployment.State);
        Assert.Contains(activities, activity => activity.OperationName == "onboarding.chat");
        Assert.Contains(activities, activity => activity.OperationName == "experience.approve");
        var (_, agentActivity) = await service.GetSessionActivityAsync(created.Biller.BillerId, CancellationToken.None);
        Assert.Contains(agentActivity, item => item.AgentId == "experience-designer" && item.Status == AgentActivityStatus.Completed);
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

    [Fact]
    public async Task ChatChangesExperiencePreferencesWithoutChangingPaymentRails()
    {
        var service = CreateService();
        var created = await service.CreateAsync(CreateRequest(), CancellationToken.None);

        var chat = await service.SendMessageAsync(
            created.Biller.BillerId,
            new("Disable AutoPay, disable account history, and remove card."),
            CancellationToken.None);

        Assert.Equal(["card", "ach"], chat.Draft!.Definition.EnabledPaymentCapabilities);
        Assert.False(chat.Draft.Definition.Preferences!.OfferAutopay);
        Assert.False(chat.Draft.Definition.Preferences.SelfServiceHistory);
        Assert.Equal(["ach"], chat.Draft.Definition.Preferences.AcceptedMethods);
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
