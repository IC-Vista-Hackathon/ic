using System.Text.Json;
using System.Text.Json.Serialization;
using Pronto.Agentic.Orchestration.Execution;
using Pronto.BillerExperience.Api.Application;
using Pronto.BillerExperience.Api.Domain;
using Pronto.BillerExperience.Api.Infrastructure.AI;
using Pronto.BillerExperience.Api.Infrastructure.Persistence;
using Pronto.BillerExperience.Api.Infrastructure.SupportingServices;
using Pronto.BillerExperience.Contracts.V1.Billers;
using Pronto.BillerExperience.Contracts.V1.Deployments;
using Pronto.BillerExperience.Contracts.V1.Experiences;
using Pronto.BillerExperience.Contracts.V1.Onboarding;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Xunit;

namespace Pronto.BillerExperience.Api.Tests;

public sealed class BillerOnboardingRegressionTests
{
    [Fact]
    public async Task ApprovedRevisionCannotBeChangedThroughChat()
    {
        var repository = new InMemoryBillerExperienceRepository();
        var service = CreateService(repository);
        var created = await service.CreateAsync(Request(), CancellationToken.None);
        var chat = await service.SendMessageAsync(
            created.Biller.BillerId,
            new("Ready for approval"),
            CancellationToken.None);
        var approved = await service.ApproveAsync(
            created.Biller.BillerId,
            new(chat.Draft!.Revision, "reviewer"),
            CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() => service.SendMessageAsync(
            created.Biller.BillerId,
            new("Change the primary color to red"),
            CancellationToken.None).AsTask());

        var unchanged = await service.GetDraftAsync(created.Biller.BillerId, CancellationToken.None);
        Assert.Equal(ExperienceRevisionState.Approved, unchanged.State);
        Assert.Equal(approved.ETag, unchanged.ETag);
        Assert.Equal(approved.Definition, unchanged.Definition);
    }

    [Fact]
    public void ExpectedETagUsesStableWireName()
    {
        var request = System.Text.Json.JsonSerializer.Deserialize<UpdateExperienceRequest>(
            """{"definition":null,"expected_etag":"etag-123"}""",
            ApiJsonOptions());

        Assert.Equal("etag-123", request!.ExpectedETag);
    }

    [Fact]
    public async Task StaleDraftETagIsRejected()
    {
        var repository = new InMemoryBillerExperienceRepository();
        var service = CreateService(repository);
        var created = await service.CreateAsync(Request(), CancellationToken.None);
        var first = await service.UpdateDraftAsync(
            created.Biller.BillerId,
            new(created.Draft.Definition, created.Draft.ETag),
            CancellationToken.None);

        await Assert.ThrowsAsync<ConcurrencyException>(() => service.UpdateDraftAsync(
            created.Biller.BillerId,
            new(first.Definition, created.Draft.ETag),
            CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task ExperienceRecordMapsDocumentETag()
    {
        var repository = new InMemoryBillerExperienceRepository();
        var created = await CreateService(repository).CreateAsync(Request(), CancellationToken.None);
        var record = await repository.GetLatestExperienceAsync(
            created.Biller.BillerId,
            CancellationToken.None);

        var json = JsonConvert.SerializeObject(record);
        var deserialized = JsonConvert.DeserializeObject<ExperienceRecord>(json);

        Assert.Contains("\"_etag\"", json, StringComparison.Ordinal);
        Assert.Equal(record!.ETag, deserialized!.ETag);
    }

    [Fact]
    public async Task FailedInvoiceSeedRemovesPartialCreationAndReleasesSlug()
    {
        var repository = new InMemoryBillerExperienceRepository();
        var seeder = new FailingInvoiceSeeder();
        var service = CreateService(repository, seeder);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateAsync(Request(), CancellationToken.None).AsTask());

        Assert.Null(await repository.GetBillerAsync(seeder.BillerId!, CancellationToken.None));
        Assert.Null(await repository.GetLatestExperienceAsync(seeder.BillerId!, CancellationToken.None));
        Assert.Null(await repository.GetRunAsync(seeder.BillerId!, "onboarding", CancellationToken.None));

        var retried = await CreateService(repository).CreateAsync(Request(), CancellationToken.None);
        Assert.Equal(Request().Slug, retried.Biller.Slug);
    }

    [Fact]
    public async Task UndefinedActionTypeBlocksApproval()
    {
        var repository = new InMemoryBillerExperienceRepository();
        var service = CreateService(repository);
        var created = await service.CreateAsync(Request(), CancellationToken.None);
        var invalidDefinition = created.Draft.Definition with
        {
            Ui = created.Draft.Definition.Ui! with
            {
                Actions =
                [
                    new ExperienceAction(
                        "unsupported",
                        "Unsupported",
                        (ExperienceActionType)999)
                ]
            }
        };

        var updated = await service.UpdateDraftAsync(
            created.Biller.BillerId,
            new(invalidDefinition, created.Draft.ETag),
            CancellationToken.None);

        Assert.Contains(updated.Findings!, finding => finding.Code == "ACTION_TYPE_INVALID");
        var exception = await Assert.ThrowsAsync<ExperienceValidationException>(
            () => service.ApproveAsync(
                created.Biller.BillerId,
                new(updated.Revision, "reviewer"),
                CancellationToken.None).AsTask());
        Assert.Contains(exception.Findings, finding => finding.Code == "ACTION_TYPE_INVALID");
    }

    [Fact]
    public void NumericActionTypeIsRejectedByJsonContract()
    {
        Assert.Throws<System.Text.Json.JsonException>(() =>
            System.Text.Json.JsonSerializer.Deserialize<ExperienceAction>(
                """{"id":"bad","label":"Bad","action":999,"variant":"primary"}""",
                ApiJsonOptions()));
    }

    [Fact]
    public async Task PublishRetryRepairsInterruptedStateBeforeCreatingDeployment()
    {
        var inner = new InMemoryBillerExperienceRepository();
        var repository = new FailOncePublishingRunRepository(inner);
        var service = CreateService(repository);
        var created = await service.CreateAsync(Request(), CancellationToken.None);
        var chat = await service.SendMessageAsync(
            created.Biller.BillerId,
            new("Ready for approval"),
            CancellationToken.None);
        var approved = await service.ApproveAsync(
            created.Biller.BillerId,
            new(chat.Draft!.Revision, "reviewer"),
            CancellationToken.None);
        var request = new PublishExperienceRequest(created.Biller.BillerId, approved.Revision);

        await Assert.ThrowsAsync<IOException>(
            () => service.PublishAsync(created.Biller.BillerId, request, CancellationToken.None).AsTask());
        Assert.Null(await inner.GetDeploymentAsync(
            created.Biller.BillerId,
            "deployment-1",
            CancellationToken.None));

        var deployment = await service.PublishAsync(
            created.Biller.BillerId,
            request,
            CancellationToken.None);
        var experience = await inner.GetLatestExperienceAsync(
            created.Biller.BillerId,
            CancellationToken.None);
        var run = await inner.GetRunAsync(
            created.Biller.BillerId,
            "onboarding",
            CancellationToken.None);

        Assert.Equal(DeploymentState.Requested, deployment.State);
        Assert.Equal(ExperienceRevisionState.Publishing, experience!.State);
        Assert.Equal(OnboardingSessionState.Publishing, run!.State);
    }

    [Fact]
    public async Task ExistingActiveDeploymentRepairsPublicationState()
    {
        var repository = new InMemoryBillerExperienceRepository();
        var service = CreateService(repository);
        var created = await service.CreateAsync(Request(), CancellationToken.None);
        var chat = await service.SendMessageAsync(
            created.Biller.BillerId,
            new("Ready for approval"),
            CancellationToken.None);
        var approved = await service.ApproveAsync(
            created.Biller.BillerId,
            new(chat.Draft!.Revision, "reviewer"),
            CancellationToken.None);
        await repository.CreateDeploymentAsync(
            new DeploymentRecord(
                "deployment-1",
                created.Biller.BillerId,
                1,
                "applying",
                DateTimeOffset.UtcNow),
            CancellationToken.None);

        var deployment = await service.PublishAsync(
            created.Biller.BillerId,
            new(created.Biller.BillerId, approved.Revision),
            CancellationToken.None);
        var experience = await repository.GetLatestExperienceAsync(
            created.Biller.BillerId,
            CancellationToken.None);
        var run = await repository.GetRunAsync(
            created.Biller.BillerId,
            "onboarding",
            CancellationToken.None);

        Assert.Equal(DeploymentState.Applying, deployment.State);
        Assert.Equal(ExperienceRevisionState.Publishing, experience!.State);
        Assert.Equal(OnboardingSessionState.Publishing, run!.State);
    }

    private static JsonSerializerOptions ApiJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower
        };
        options.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
        return options;
    }

    private static BillerOnboardingService CreateService(
        IBillerExperienceRepository repository,
        IInvoiceSeeder? seeder = null) =>
        new(
            repository,
            new DeterministicExperienceDraftGenerator(
                NullLogger<DeterministicExperienceDraftGenerator>.Instance),
            new OrchestrationRunner(),
            NullLogger<BillerOnboardingService>.Instance,
            invoiceSeeder: seeder);

    private static CreateBillerRequest Request() =>
        new(
            "City of Vista",
            "city-of-vista",
            "Utility",
            "02110",
            new Uri("https://vista.example"));

    private sealed class FailingInvoiceSeeder : IInvoiceSeeder
    {
        public string? BillerId { get; private set; }

        public ValueTask SeedAsync(
            string billerId,
            string billType,
            CancellationToken cancellationToken)
        {
            BillerId = billerId;
            return ValueTask.FromException(new InvalidOperationException("seed failed"));
        }
    }

    private sealed class FailOncePublishingRunRepository(
        IBillerExperienceRepository inner) : IBillerExperienceRepository
    {
        private int failPublishingRun = 1;

        public ValueTask<BillerRecord> CreateBillerAsync(
            BillerRecord biller,
            CancellationToken cancellationToken) =>
            inner.CreateBillerAsync(biller, cancellationToken);

        public ValueTask<BillerRecord?> GetBillerAsync(
            string billerId,
            CancellationToken cancellationToken) =>
            inner.GetBillerAsync(billerId, cancellationToken);

        public ValueTask<bool> TryReserveSlugAsync(
            string slug,
            string billerId,
            CancellationToken cancellationToken) =>
            inner.TryReserveSlugAsync(slug, billerId, cancellationToken);

        public ValueTask ReleaseSlugAsync(
            string slug,
            string billerId,
            CancellationToken cancellationToken) =>
            inner.ReleaseSlugAsync(slug, billerId, cancellationToken);

        public ValueTask<BillerRecord> SaveBillerAsync(
            BillerRecord biller,
            CancellationToken cancellationToken) =>
            inner.SaveBillerAsync(biller, cancellationToken);

        public ValueTask<ExperienceRecord?> GetLatestExperienceAsync(
            string billerId,
            CancellationToken cancellationToken) =>
            inner.GetLatestExperienceAsync(billerId, cancellationToken);

        public ValueTask<ExperienceRecord> SaveExperienceAsync(
            ExperienceRecord experience,
            string? expectedETag,
            CancellationToken cancellationToken) =>
            inner.SaveExperienceAsync(experience, expectedETag, cancellationToken);

        public ValueTask<OnboardingRunRecord?> GetRunAsync(
            string billerId,
            string runId,
            CancellationToken cancellationToken) =>
            inner.GetRunAsync(billerId, runId, cancellationToken);

        public ValueTask<OnboardingRunRecord> SaveRunAsync(
            OnboardingRunRecord run,
            string? expectedETag,
            CancellationToken cancellationToken)
        {
            if (run.State == OnboardingSessionState.Publishing &&
                Interlocked.Exchange(ref failPublishingRun, 0) == 1)
            {
                return ValueTask.FromException<OnboardingRunRecord>(
                    new IOException("run persistence failed"));
            }

            return inner.SaveRunAsync(run, expectedETag, cancellationToken);
        }

        public ValueTask AppendAgentActivityAsync(
            string billerId,
            string runId,
            AgentActivityEvent activity,
            CancellationToken cancellationToken) =>
            inner.AppendAgentActivityAsync(billerId, runId, activity, cancellationToken);

        public ValueTask<IReadOnlyList<AgentActivityEvent>> GetAgentActivityAsync(
            string billerId,
            string runId,
            CancellationToken cancellationToken) =>
            inner.GetAgentActivityAsync(billerId, runId, cancellationToken);

        public ValueTask<AgentContextRecord?> GetAgentContextAsync(
            string billerId,
            string runId,
            CancellationToken cancellationToken) =>
            inner.GetAgentContextAsync(billerId, runId, cancellationToken);

        public ValueTask<AgentContextRecord> SaveAgentContextAsync(
            AgentContextRecord context,
            string? expectedETag,
            CancellationToken cancellationToken) =>
            inner.SaveAgentContextAsync(context, expectedETag, cancellationToken);

        public ValueTask<DeploymentRecord?> GetDeploymentAsync(
            string billerId,
            string deploymentId,
            CancellationToken cancellationToken) =>
            inner.GetDeploymentAsync(billerId, deploymentId, cancellationToken);

        public ValueTask<DeploymentRecord> CreateDeploymentAsync(
            DeploymentRecord deployment,
            CancellationToken cancellationToken) =>
            inner.CreateDeploymentAsync(deployment, cancellationToken);

        public ValueTask PurgeByBillerAsync(
            string billerId,
            CancellationToken cancellationToken) =>
            inner.PurgeByBillerAsync(billerId, cancellationToken);
    }
}
