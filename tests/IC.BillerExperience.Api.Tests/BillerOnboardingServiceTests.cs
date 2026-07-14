using System.Diagnostics;
using System.Net;
using System.Text;
using IC.BillerExperience.Api.Application;
using IC.BillerExperience.Api.Infrastructure;
using IC.BillerExperience.Api.Infrastructure.AI;
using IC.BillerExperience.Api.Infrastructure.Persistence;
using IC.BillerExperience.Api.Infrastructure.SupportingServices;
using IC.BillerExperience.Contracts.V1.Billers;
using IC.BillerExperience.Contracts.V1.Deployments;
using IC.BillerExperience.Contracts.V1.Experiences;
using IC.BillerExperience.Contracts.V1.Onboarding;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Xunit;

namespace IC.BillerExperience.Api.Tests;

public sealed class BillerOnboardingServiceTests
{
    [Fact]
    public void CosmosRecordsUseRequiredWirePropertyNames()
    {
        var record = new IC.BillerExperience.Api.Domain.BillerRecord(
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

    [Fact]
    public async Task CreatingBillerSeedsItsInvoiceData()
    {
        var seeder = new RecordingInvoiceSeeder();
        var service = CreateService(seeder);

        var created = await service.CreateAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal(created.Biller.BillerId, seeder.BillerId);
        Assert.Equal("Utility", seeder.BillType);
    }

    [Fact]
    public async Task InvoiceSeedFailureFailsBillerCreation()
    {
        var service = CreateService(new RecordingInvoiceSeeder(new InvalidOperationException("seed failed")));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.CreateAsync(CreateRequest(), CancellationToken.None));

        Assert.Equal("seed failed", exception.Message);
    }

    [Fact]
    public async Task HttpInvoiceSeederUsesSnakeCaseContractAndFixedPreviewAccount()
    {
        var handler = new RecordingHttpHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://invoice.test/") };
        var seeder = new HttpInvoiceSeeder(client, NullLogger<HttpInvoiceSeeder>.Instance);

        await seeder.SeedAsync("biller-1", "Utility", CancellationToken.None);

        Assert.Contains("\"account_number\":\"4421\"", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("\"bill_type\":\"Utility\"", handler.RequestBody, StringComparison.Ordinal);
    }

    private static BillerOnboardingService CreateService(IInvoiceSeeder? seeder = null)
    {
        var repository = new InMemoryBillerExperienceRepository();
        var generator = new DeterministicExperienceDraftGenerator(NullLogger<DeterministicExperienceDraftGenerator>.Instance);
        return new(repository, generator, NullLogger<BillerOnboardingService>.Instance, seeder);
    }

    private static CreateBillerRequest CreateRequest() =>
        new("City of Vista", "city-of-vista", "Utility", "02110", new Uri("https://vista.example"));

    private sealed class RecordingInvoiceSeeder(Exception? failure = null) : IInvoiceSeeder
    {
        public string? BillerId { get; private set; }
        public string? BillType { get; private set; }

        public ValueTask SeedAsync(string billerId, string billType, CancellationToken cancellationToken)
        {
            if (failure is not null) throw failure;
            BillerId = billerId;
            BillType = billType;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingHttpHandler : HttpMessageHandler
    {
        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(
                    "{\"seeded\":4,\"account_number\":\"4421\",\"invoices\":[]}",
                    Encoding.UTF8,
                    "application/json"),
            };
        }
    }
}
