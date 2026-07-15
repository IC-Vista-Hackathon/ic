using System.Diagnostics;
using System.Net;
using System.Text;
using Pronto.Agentic.Orchestration.Abstractions;
using Pronto.Agentic.Orchestration.Execution;
using Pronto.BillerExperience.Api.Application;
using Pronto.BillerExperience.Api.Application.Compliance;
using Pronto.BillerExperience.Api.Infrastructure;
using Pronto.BillerExperience.Api.Infrastructure.AI;
using Pronto.BillerExperience.Api.Infrastructure.Persistence;
using Pronto.BillerExperience.Api.Infrastructure.Research;
using Pronto.BillerExperience.Api.Infrastructure.SupportingServices;
using Pronto.BillerExperience.Contracts.V1.Billers;
using Pronto.BillerExperience.Contracts.V1.Deployments;
using Pronto.BillerExperience.Contracts.V1.Experiences;
using Pronto.BillerExperience.Contracts.V1.Onboarding;
using Pronto.BillerExperience.Contracts.V1.Research;
using Pronto.ServiceDefaults;
using Microsoft.Extensions.Logging;
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
    public async Task PublishRevalidatesTheExactApprovedRevision()
    {
        var compliance = new RecordingComplianceReviewService();
        var service = CreateService(complianceReviewService: compliance);
        var created = await service.CreateAsync(CreateRequest(), CancellationToken.None);
        var chat = await service.SendMessageAsync(
            created.Biller.BillerId,
            new("Use #174A5B and get ready to publish."),
            CancellationToken.None);
        var approved = await service.ApproveAsync(
            created.Biller.BillerId,
            new(chat.Draft!.Revision, "test-user"),
            CancellationToken.None);

        await service.PublishAsync(
            created.Biller.BillerId,
            new(created.Biller.BillerId, approved.Revision),
            CancellationToken.None);

        var publish = Assert.Single(compliance.Reviews, review => review.Stage == ComplianceReviewStage.Publish);
        Assert.Equal(approved.Revision, chat.Draft.Revision);
        Assert.Equal("#174A5B", publish.Definition.Brand.PrimaryColor);
        Assert.Equal(created.Biller.BillerId, publish.Definition.BillerId);
    }

    [Fact]
    public async Task PublishFailsWhenRevalidationFindsANewBlockingIssue()
    {
        var compliance = new RecordingComplianceReviewService(blockPublish: true);
        var service = CreateService(complianceReviewService: compliance);
        var created = await service.CreateAsync(CreateRequest(), CancellationToken.None);
        var chat = await service.SendMessageAsync(
            created.Biller.BillerId,
            new("Ready for review"),
            CancellationToken.None);
        var approved = await service.ApproveAsync(
            created.Biller.BillerId,
            new(chat.Draft!.Revision, "test-user"),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ExperienceValidationException>(async () =>
            await service.PublishAsync(
                created.Biller.BillerId,
                new(created.Biller.BillerId, approved.Revision),
                CancellationToken.None));

        Assert.Contains(exception.Findings, finding => finding.Code == "PUBLISH_REVALIDATION_FAILED");
        Assert.Contains(compliance.Reviews, review => review.Stage == ComplianceReviewStage.Publish);
    }

    [Fact]
    public async Task ApprovalFailureReturnsTheBlockingFindings()
    {
        var service = CreateService();
        var created = await service.CreateAsync(CreateRequest(), CancellationToken.None);
        var invalidDefinition = created.Draft.Definition with
        {
            Brand = created.Draft.Definition.Brand with { PrimaryColor = "not-a-color" }
        };
        var updated = await service.UpdateDraftAsync(
            created.Biller.BillerId,
            new UpdateExperienceRequest(invalidDefinition, created.Draft.ETag),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ExperienceValidationException>(async () =>
            await service.ApproveAsync(
                created.Biller.BillerId,
                new ApproveExperienceRequest(updated.Revision, "test-user"),
                CancellationToken.None));

        Assert.Contains(exception.Findings, finding => finding.Code == "BRAND_COLOR_INVALID");
    }

    [Fact]
    public async Task PublishPersistsW3CTraceparentFromCurrentActivity()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == BillerExperienceTelemetry.SourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        var repository = new InMemoryBillerExperienceRepository();
        var service = new BillerOnboardingService(
            repository,
            new DeterministicExperienceDraftGenerator(NullLogger<DeterministicExperienceDraftGenerator>.Instance),
            new OrchestrationRunner(),
            NullLogger<BillerOnboardingService>.Instance);
        var created = await service.CreateAsync(CreateRequest(), CancellationToken.None);
        var chat = await service.SendMessageAsync(created.Biller.BillerId, new("Ready for review"), CancellationToken.None);
        var approved = await service.ApproveAsync(created.Biller.BillerId, new(chat.Draft!.Revision, "test-user"), CancellationToken.None);

        var deployment = await service.PublishAsync(
            created.Biller.BillerId,
            new PublishExperienceRequest(created.Biller.BillerId, approved.Revision),
            CancellationToken.None);

        var record = await repository.GetDeploymentAsync(created.Biller.BillerId, deployment.DeploymentId, CancellationToken.None);
        Assert.NotNull(record);
        Assert.Matches("^00-[0-9a-f]{32}-[0-9a-f]{16}-0[01]$", record!.Traceparent);
    }

    [Fact]
    public async Task InvoiceSeederPropagatesCorrelationHeadersFromRequestActivity()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == BillerExperienceTelemetry.SourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        // Ambient inbound-request activity carrying the correlation id set by RequestObservabilityMiddleware.
        using var request = new Activity("request");
        request.SetIdFormat(ActivityIdFormat.W3C);
        request.Start();
        request.SetTag("ic.correlation_id", "corr-xyz");

        var handler = new RecordingHttpHandler();
        using var client = new HttpClient(new CorrelationPropagationHandler { InnerHandler = handler })
        {
            BaseAddress = new Uri("http://invoice.test/"),
        };
        var seeder = new HttpInvoiceSeeder(client, NullLogger<HttpInvoiceSeeder>.Instance);

        await seeder.SeedAsync("biller-77", "Utility", CancellationToken.None);

        Assert.Equal("corr-xyz", handler.CorrelationHeader);
        Assert.Equal("biller-77", handler.BillerHeader);
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
    public async Task ChatRunsThroughNamedOrchestrationWorkflow()
    {
        var runner = new RecordingOrchestrationRunner();
        var service = CreateService(runner: runner);
        var created = await service.CreateAsync(CreateRequest(), CancellationToken.None);

        await service.SendMessageAsync(created.Biller.BillerId, new("Ready for review"), CancellationToken.None);

        Assert.Equal("biller-experience-chat-turn", runner.WorkflowName);
        Assert.Equal(created.Biller.BillerId, runner.Context?.BillerId);
    }

    [Fact]
    public async Task AgentFailureIsSurfacedAndRecorded()
    {
        var logger = new RecordingLogger<BillerOnboardingService>();
        var service = CreateService(generator: new FailingDraftGenerator(), logger: logger);
        var created = await service.CreateAsync(CreateRequest(), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.SendMessageAsync(created.Biller.BillerId, new("Trigger failure"), CancellationToken.None));

        Assert.Equal("designer failed", exception.Message);
        var (_, activity) = await service.GetSessionActivityAsync(created.Biller.BillerId, CancellationToken.None);
        Assert.Contains(activity, item => item.AgentId == "experience-designer" && item.Status == AgentActivityStatus.Failed);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.EventId.Id == 9101);
    }

    [Fact]
    public async Task DeterministicDesignerResolvesNamedPrimaryColorFromChat()
    {
        var service = CreateService();
        var created = await service.CreateAsync(CreateRequest(), CancellationToken.None);

        var response = await service.SendMessageAsync(
            created.Biller.BillerId,
            new("change the primary color from blue to red"),
            CancellationToken.None);

        // The target after "to" wins over the source color, and the fallback maps the name to an
        // accessible hex instead of ignoring anything that isn't already a hex code.
        Assert.Equal("#c1121f", response.Draft?.Definition.Brand.PrimaryColor);
        // The deterministic designer is surfaced so the Studio can flag when the live model didn't run.
        Assert.Equal("deterministic", response.GenerationMode);
    }

    [Fact]
    public async Task DeterministicDesignerHonorsHeadingRequestAlongsideColor()
    {
        var service = CreateService();
        var created = await service.CreateAsync(CreateRequest(), CancellationToken.None);

        var response = await service.SendMessageAsync(
            created.Biller.BillerId,
            new("Change the primary color to purple and make the heading say Welcome back"),
            CancellationToken.None);

        // Both clearly-expressed requests are honored: the color and the heading text.
        Assert.Equal("#6d28d9", response.Draft?.Definition.Brand.PrimaryColor);
        Assert.Equal("Welcome back", response.Draft?.Definition.Content.Heading);
    }

    [Fact]
    public async Task DeterministicDesignerLeavesHeadingUnchangedWhenNotRequested()
    {
        var service = CreateService();
        var created = await service.CreateAsync(CreateRequest(), CancellationToken.None);
        var original = created.Draft.Definition.Content.Heading;

        var response = await service.SendMessageAsync(
            created.Biller.BillerId,
            new("change the primary color to green"),
            CancellationToken.None);

        // A request that never mentions the heading must not fabricate a heading change — otherwise
        // the Studio's proposed-revision summary would describe an edit the biller never asked for.
        Assert.Equal("#197d00", response.Draft?.Definition.Brand.PrimaryColor);
        Assert.Equal(original, response.Draft?.Definition.Content.Heading);
    }

    [Fact]
    public async Task MissingWebsitePassesExplicitSkippedResearchToDesigner()
    {
        var generator = new CapturingDraftGenerator();
        var service = CreateService(generator: generator);
        var created = await service.CreateAsync(CreateRequest() with { Website = null }, CancellationToken.None);

        await service.SendMessageAsync(created.Biller.BillerId, new("Ready for review"), CancellationToken.None);

        Assert.Equal(ResearchOutcome.Skipped, generator.Research?.Outcome);
        Assert.Equal("research.not_configured", generator.Research?.ErrorCode);
        var (_, activity) = await service.GetSessionActivityAsync(created.Biller.BillerId, CancellationToken.None);
        Assert.Contains(activity, item => item.AgentId == "biller-research" && item.Status == AgentActivityStatus.Skipped);
    }

    [Fact]
    public async Task MissingWebsiteStillInvokesConfiguredResearchCoordinatorWithBillerContext()
    {
        var generator = new CapturingDraftGenerator();
        var coordinator = new StubResearchCoordinator(new BillerResearchResponse(
            ResearchOutcome.Completed, [], [], []));
        var service = CreateService(generator: generator, researchCoordinator: coordinator);
        var created = await service.CreateAsync(CreateRequest() with { Website = null }, CancellationToken.None);

        await service.SendMessageAsync(created.Biller.BillerId, new("Ready for review"), CancellationToken.None);

        Assert.NotNull(coordinator.Request);
        Assert.Null(coordinator.Request.Website);
        Assert.Equal(CreateRequest().DisplayName, coordinator.Request.BillerName);
        Assert.Equal(CreateRequest().BillType, coordinator.Request.BillType);
        Assert.Equal(CreateRequest().PostalCode, coordinator.Request.PostalCode);
        Assert.Equal(ResearchOutcome.Completed, generator.Research?.Outcome);
    }

    [Fact]
    public async Task OptionalResearchFailureContinuesAsVisibleDegradedResult()
    {
        var generator = new CapturingDraftGenerator();
        var coordinator = new StubResearchCoordinator(new BillerResearchResponse(
            ResearchOutcome.Failed, [], [], ["research.request_failed"], "research.request_failed", true));
        var service = CreateService(generator: generator, researchCoordinator: coordinator);
        var created = await service.CreateAsync(CreateRequest(), CancellationToken.None);

        var response = await service.SendMessageAsync(
            created.Biller.BillerId, new("Ready for review"), CancellationToken.None);

        Assert.NotNull(response.Draft);
        Assert.Equal(ResearchOutcome.Degraded, generator.Research?.Outcome);
        var (_, activity) = await service.GetSessionActivityAsync(created.Biller.BillerId, CancellationToken.None);
        Assert.Contains(activity, item => item.AgentId == "biller-research" && item.Status == AgentActivityStatus.Degraded);
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

    private static BillerOnboardingService CreateService(
        IInvoiceSeeder? seeder = null,
        IOrchestrationRunner? runner = null,
        IExperienceDraftGenerator? generator = null,
        ILogger<BillerOnboardingService>? logger = null,
        IBillerResearchCoordinator? researchCoordinator = null,
        IComplianceReviewService? complianceReviewService = null)
    {
        var repository = new InMemoryBillerExperienceRepository();
        generator ??= new DeterministicExperienceDraftGenerator(NullLogger<DeterministicExperienceDraftGenerator>.Instance);
        return new(repository, generator, runner ?? new OrchestrationRunner(),
            logger ?? NullLogger<BillerOnboardingService>.Instance, researchCoordinator, seeder,
            complianceReviewService: complianceReviewService);
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
        public string? CorrelationHeader { get; private set; }
        public string? BillerHeader { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            CorrelationHeader = request.Headers.TryGetValues(
                RequestObservabilityMiddleware.CorrelationHeader, out var correlation) ? correlation.FirstOrDefault() : null;
            BillerHeader = request.Headers.TryGetValues(
                RequestObservabilityMiddleware.BillerHeader, out var biller) ? biller.FirstOrDefault() : null;
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(
                    "{\"seeded\":4,\"account_number\":\"4421\",\"invoices\":[]}",
                    Encoding.UTF8,
                    "application/json"),
            };
        }
    }

    private sealed class RecordingOrchestrationRunner : IOrchestrationRunner
    {
        public string? WorkflowName { get; private set; }
        public OrchestrationContext? Context { get; private set; }

        public ValueTask<TOutput> RunAsync<TInput, TOutput>(
            IOrchestrationWorkflow<TInput, TOutput> workflow,
            TInput input,
            OrchestrationContext context,
            CancellationToken cancellationToken = default)
        {
            WorkflowName = workflow.Name;
            Context = context;
            return workflow.ExecuteAsync(input, context, cancellationToken);
        }
    }

    private sealed class FailingDraftGenerator : IExperienceDraftGenerator
    {
        public string Provider => "failing-test";

        public ValueTask<DraftGenerationResult> GenerateAsync(
            Pronto.BillerExperience.Api.Domain.BillerRecord biller,
            Pronto.BillerExperience.Api.Domain.ExperienceRecord current,
            IReadOnlyList<OnboardingChatMessage> messages,
            BillerResearchResponse research,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<DraftGenerationResult>(new InvalidOperationException("designer failed"));
    }

    private sealed class CapturingDraftGenerator : IExperienceDraftGenerator
    {
        private readonly DeterministicExperienceDraftGenerator inner =
            new(NullLogger<DeterministicExperienceDraftGenerator>.Instance);

        public string Provider => inner.Provider;
        public BillerResearchResponse? Research { get; private set; }

        public ValueTask<DraftGenerationResult> GenerateAsync(
            Pronto.BillerExperience.Api.Domain.BillerRecord biller,
            Pronto.BillerExperience.Api.Domain.ExperienceRecord current,
            IReadOnlyList<OnboardingChatMessage> messages,
            BillerResearchResponse research,
            CancellationToken cancellationToken)
        {
            Research = research;
            return inner.GenerateAsync(biller, current, messages, research, cancellationToken);
        }
    }

    private sealed class StubResearchCoordinator(BillerResearchResponse response) : IBillerResearchCoordinator
    {
        public BillerResearchRequest? Request { get; private set; }

        public Task<BillerResearchResponse> ResearchAsync(
            BillerResearchRequest request,
            ResearchExecutionContext? executionContext = null,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(response);
        }
    }

    private sealed class RecordingComplianceReviewService(bool blockPublish = false) : IComplianceReviewService
    {
        public List<(ComplianceReviewStage Stage, BillerExperienceDefinition Definition)> Reviews { get; } = [];

        public ValueTask<IReadOnlyList<ComplianceFinding>> ReviewAsync(
            Pronto.BillerExperience.Api.Domain.BillerRecord biller,
            BillerExperienceDefinition definition,
            ComplianceReviewStage stage,
            CancellationToken cancellationToken)
        {
            Reviews.Add((stage, definition));
            IReadOnlyList<ComplianceFinding> findings =
                blockPublish && stage == ComplianceReviewStage.Publish
                    ? [new("PUBLISH_REVALIDATION_FAILED", "Publish-time review failed.", ComplianceFindingSeverity.Blocking)]
                    : [];
            return ValueTask.FromResult(findings);
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, EventId EventId)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, eventId));
    }
}
