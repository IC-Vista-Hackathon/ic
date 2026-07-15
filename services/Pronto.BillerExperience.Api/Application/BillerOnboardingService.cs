using System.Diagnostics;
using System.Text.RegularExpressions;
using Pronto.Agentic.Orchestration.Abstractions;
using Pronto.BillerExperience.Api.Application.Orchestration;
using Pronto.BillerExperience.Api.Application.Agents;
using Pronto.BillerExperience.Api.Application.Compliance;
using Pronto.BillerExperience.Api.Configuration;
using Pronto.BillerExperience.Api.Domain;
using Pronto.BillerExperience.Api.Infrastructure;
using Pronto.BillerExperience.Api.Infrastructure.AI;
using Pronto.BillerExperience.Api.Infrastructure.Persistence;
using Pronto.BillerExperience.Api.Infrastructure.Research;
using Pronto.BillerExperience.Api.Infrastructure.SupportingServices;
using Pronto.BillerExperience.Contracts.V1.Billers;
using Pronto.BillerExperience.Contracts.V1.Deployments;
using Pronto.BillerExperience.Contracts.V1.Experiences;
using Pronto.BillerExperience.Contracts.V1.Onboarding;

namespace Pronto.BillerExperience.Api.Application;

public sealed partial class BillerOnboardingService(
    IBillerExperienceRepository repository,
    IExperienceDraftGenerator draftGenerator,
    IOrchestrationRunner orchestrationRunner,
    ILogger<BillerOnboardingService> logger,
    IBillerResearchCoordinator? researchCoordinator = null,
    IInvoiceSeeder? invoiceSeeder = null,
    AgentContextService? agentContextService = null,
    IComplianceReviewService? complianceReviewService = null)
{
    private const string RunId = "onboarding";
    private readonly IComplianceReviewService _compliance = complianceReviewService ?? CreateDefaultComplianceService();

    public async ValueTask<(BillerResponse Biller, OnboardingSessionResponse Session, ExperienceRevisionResponse Draft)> CreateAsync(
        CreateBillerRequest request,
        CancellationToken cancellationToken)
    {
        using var activity = StartActivity("biller.create");
        // Forgive-and-normalize: casing/whitespace are fixed for the caller; validation then
        // rejects only what normalization can't repair (bad characters, bad length).
        var normalizedSlug = request.Slug.Trim().ToLowerInvariant();
        ValidateCreateRequest(request with { Slug = normalizedSlug });
        var id = Guid.NewGuid().ToString("N");
        activity?.SetTag("ic.biller_id", id);
        var now = DateTimeOffset.UtcNow;
        var slug = await ReserveSlugAsync(normalizedSlug, cancellationToken);
        var biller = new BillerRecord(
            id,
            request.DisplayName.Trim(),
            slug,
            request.BillType.Trim(),
            request.PostalCode.Trim(),
            request.Website,
            request.Brand,
            request.Support,
            request.PaymentRails ?? Array.Empty<PaymentRailReference>(),
            BillerStatus.Prospect,
            now);
        var definition = CreateInitialDefinition(biller);
        var experience = new ExperienceRecord(
            "config-1",
            id,
            1,
            ExperienceRevisionState.Draft,
            definition,
            Array.Empty<ComplianceFinding>(),
            now);
        var run = new OnboardingRunRecord(
            RunId,
            id,
            "biller-onboarding",
            OnboardingSessionState.CollectingInformation,
            0,
            [new OnboardingChatMessage("assistant", $"Welcome! I created a starting preview for {biller.Name}. Tell me what you want customers to feel or change.", now)],
            ["review_brand", "review_legal_links", "review_payment_methods"],
            now);

        await repository.CreateBillerAsync(biller, cancellationToken);
        // Note: check-then-create, not an atomic reservation — adequate while onboarding
        // volume is demo-scale; a slug reservation document makes this race-free later.
        var savedExperience = await repository.SaveExperienceAsync(experience, null, cancellationToken);
        var savedRun = await repository.SaveRunAsync(run, null, cancellationToken);
        if (agentContextService is not null)
        {
            await agentContextService.EnsureAsync(
                id,
                savedRun.Id,
                $"Create, review, approve, and publish a safe branded payment experience for {biller.Name}.",
                cancellationToken);
        }
        await (invoiceSeeder ?? new NullInvoiceSeeder()).SeedAsync(id, biller.BillType, cancellationToken);
        LogBillerCreated(logger, id, savedRun.Id, draftGenerator.Provider);
        return (Map(biller), Map(savedRun), Map(savedExperience));
    }

    public async ValueTask<BillerResponse> GetBillerAsync(string billerId, CancellationToken cancellationToken)
    {
        var biller = await GetRequiredBillerAsync(billerId, cancellationToken);
        return Map(biller);
    }

    public async ValueTask<OnboardingChatResponse> SendMessageAsync(
        string billerId,
        SendOnboardingMessageRequest request,
        CancellationToken cancellationToken)
    {
        using var activity = StartActivity("onboarding.chat");
        activity?.SetTag("ic.biller_id", billerId);
        if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Length > 4000)
        {
            LogValidationError(logger, billerId, "message", "Message must contain 1 to 4000 characters.");
            BillerExperienceTelemetry.ValidationFailures.Add(1, new KeyValuePair<string, object?>("field", "message"));
            throw new ArgumentException("Message must contain 1 to 4000 characters.");
        }

        var biller = await GetRequiredBillerAsync(billerId, cancellationToken);
        var run = await GetRequiredRunAsync(billerId, cancellationToken);
        var experience = await GetRequiredExperienceAsync(billerId, cancellationToken);
        var userMessage = new OnboardingChatMessage("user", request.Message.Trim(), DateTimeOffset.UtcNow);
        var messages = run.Messages.Append(userMessage).ToArray();
        var orchestrationContext = new OrchestrationContext(
            run.Id,
            Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N"),
            billerId,
            run.Id);
        var eventSink = new AgentActivityRepositorySink(repository, billerId, run.Id, logger);
        var workflow = new BillerExperienceChatWorkflow(
            new ExperienceDesignAgent(draftGenerator),
            new AccessibilityReviewAgent(),
            new ComplianceReviewAgent(_compliance),
            researchCoordinator,
            logger,
            researchRequired: false);
        var generated = await orchestrationRunner.RunAsync(
            workflow,
            new BillerExperienceChatWorkflowInput(biller, experience, messages, eventSink),
            orchestrationContext,
            cancellationToken);
        var assistantMessage = new OnboardingChatMessage("assistant", generated.Reply, DateTimeOffset.UtcNow);
        var nextState = generated.MissingFields.Count == 0
            ? OnboardingSessionState.DraftReady
            : OnboardingSessionState.CollectingInformation;
        var updatedExperience = experience with
        {
            Definition = generated.Definition with { BillerId = billerId },
            Findings = generated.Findings
        };
        var latestRun = await GetRequiredRunAsync(billerId, cancellationToken);
        var updatedRun = latestRun with
        {
            State = nextState,
            Step = run.Step + 1,
            Messages = messages.Append(assistantMessage).ToArray(),
            MissingFields = generated.MissingFields,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var savedExperience = await repository.SaveExperienceAsync(updatedExperience, experience.ETag, cancellationToken);
        var savedRun = await repository.SaveRunAsync(updatedRun, latestRun.ETag, cancellationToken);
        BillerExperienceTelemetry.ChatTurns.Add(1, new KeyValuePair<string, object?>("provider", draftGenerator.Provider));
        RecordTransition(run.State, nextState);
        LogChatCompleted(logger, billerId, savedRun.Id, savedRun.Step, nextState, draftGenerator.Provider);
        return new OnboardingChatResponse(generated.Reply, Map(savedRun), Map(savedExperience));
    }

    public async ValueTask<ExperienceRevisionResponse> GetDraftAsync(string billerId, CancellationToken cancellationToken) =>
        Map(await GetRequiredExperienceAsync(billerId, cancellationToken));

    public async ValueTask<ExperienceRevisionResponse> UpdateDraftAsync(
        string billerId,
        UpdateExperienceRequest request,
        CancellationToken cancellationToken)
    {
        using var activity = StartActivity("experience.update");
        activity?.SetTag("ic.biller_id", billerId);
        var current = await GetRequiredExperienceAsync(billerId, cancellationToken);
        if (current.State != ExperienceRevisionState.Draft)
        {
            LogValidationError(logger, billerId, "state", "Only draft experiences can be updated.");
            throw new ArgumentException("Only draft experiences can be updated.");
        }

        var biller = await GetRequiredBillerAsync(billerId, cancellationToken);
        var definition = request.Definition with { BillerId = billerId };
        var findings = await _compliance.ReviewAsync(
            biller,
            definition,
            ComplianceReviewStage.Draft,
            cancellationToken);
        var saved = await repository.SaveExperienceAsync(
            current with { Definition = definition, Findings = findings },
            request.ExpectedETag ?? current.ETag,
            cancellationToken);
        LogDraftUpdated(logger, billerId, saved.Version, findings.Count);
        return Map(saved);
    }

    public async ValueTask<ExperienceRevisionResponse> ApproveAsync(
        string billerId,
        ApproveExperienceRequest request,
        CancellationToken cancellationToken)
    {
        using var activity = StartActivity("experience.approve");
        activity?.SetTag("ic.biller_id", billerId);
        var experience = await GetRequiredExperienceAsync(billerId, cancellationToken);
        var biller = await GetRequiredBillerAsync(billerId, cancellationToken);
        var run = await GetRequiredRunAsync(billerId, cancellationToken);
        if (experience.Id != request.Revision)
        {
            LogValidationError(logger, billerId, "revision", "The requested revision is not current.");
            throw new ArgumentException("The requested revision is not current.");
        }

        var findings = await _compliance.ReviewAsync(
            biller,
            experience.Definition,
            ComplianceReviewStage.Approval,
            cancellationToken);
        var blockingFindings = findings
            .Where(finding => finding.Severity == ComplianceFindingSeverity.Blocking)
            .ToArray();
        if (blockingFindings.Length > 0)
        {
            foreach (var finding in blockingFindings)
            {
                LogBlockingFinding(logger, billerId, experience.Id, finding.Code, finding.Message);
            }
            throw new ExperienceValidationException(
                "This experience is not ready to publish. Resolve the listed validation items and try again.",
                blockingFindings);
        }

        var now = DateTimeOffset.UtcNow;
        var saved = await repository.SaveExperienceAsync(
            experience with { State = ExperienceRevisionState.Approved, ApprovedAt = now, Findings = findings },
            experience.ETag,
            cancellationToken);
        await repository.SaveRunAsync(
            run with { State = OnboardingSessionState.Approved, MissingFields = Array.Empty<string>(), UpdatedAt = now },
            run.ETag,
            cancellationToken);
        RecordTransition(run.State, OnboardingSessionState.Approved);
        LogExperienceApproved(logger, billerId, saved.Id, request.ApprovedBy);
        return Map(saved);
    }

    public async ValueTask<DeploymentStatusResponse> PublishAsync(
        string billerId,
        PublishExperienceRequest request,
        CancellationToken cancellationToken)
    {
        using var activity = StartActivity("experience.publish.request");
        activity?.SetTag("ic.biller_id", billerId);
        var experience = await GetRequiredExperienceAsync(billerId, cancellationToken);
        var biller = await GetRequiredBillerAsync(billerId, cancellationToken);
        var run = await GetRequiredRunAsync(billerId, cancellationToken);
        if (experience.Id != request.Revision)
        {
            LogValidationError(logger, billerId, "revision", "The requested revision is not current.");
            throw new ArgumentException("The requested revision is not current.");
        }

        var deploymentId = $"deployment-{experience.Version}";
        var existing = await repository.GetDeploymentAsync(billerId, deploymentId, cancellationToken);
        if (existing is not null)
        {
            LogDuplicatePublication(logger, billerId, request.Revision, deploymentId);
            return Map(existing);
        }

        if (experience.State != ExperienceRevisionState.Approved)
        {
            LogValidationError(logger, billerId, "state", "The current revision must be approved before publication.");
            throw new ArgumentException("The current revision must be approved before publication.");
        }

        var findings = await _compliance.ReviewAsync(
            biller,
            experience.Definition,
            ComplianceReviewStage.Publish,
            cancellationToken);
        var blockingFindings = findings
            .Where(finding => finding.Severity == ComplianceFindingSeverity.Blocking)
            .ToArray();
        if (blockingFindings.Length > 0)
        {
            foreach (var finding in blockingFindings)
            {
                LogBlockingFinding(logger, billerId, experience.Id, finding.Code, finding.Message);
            }
            throw new ExperienceValidationException(
                "This experience is not ready to publish. Resolve the listed validation items and try again.",
                blockingFindings);
        }

        var now = DateTimeOffset.UtcNow;
        var deployment = await repository.CreateDeploymentAsync(
            new DeploymentRecord(deploymentId, billerId, experience.Version, "requested", now,
                Traceparent: FormatTraceparent(Activity.Current)),
            cancellationToken);
        await repository.SaveExperienceAsync(
            experience with { State = ExperienceRevisionState.Publishing, Findings = findings },
            experience.ETag,
            cancellationToken);
        await repository.SaveRunAsync(run with { State = OnboardingSessionState.Publishing, UpdatedAt = now }, run.ETag, cancellationToken);
        RecordTransition(run.State, OnboardingSessionState.Publishing);
        LogPublicationRequested(logger, billerId, experience.Id, deployment.Id);
        return Map(deployment);
    }

    public async ValueTask<OnboardingSessionResponse> GetSessionAsync(string billerId, CancellationToken cancellationToken) =>
        Map(await GetRequiredRunAsync(billerId, cancellationToken));

    public async ValueTask<(OnboardingSessionResponse Session, IReadOnlyList<AgentActivityEvent> Activity)> GetSessionActivityAsync(
        string billerId,
        CancellationToken cancellationToken)
    {
        var run = await GetRequiredRunAsync(billerId, cancellationToken);
        var appendedActivity = await repository.GetAgentActivityAsync(billerId, run.Id, cancellationToken);
        var activity = (run.AgentActivity ?? [])
            .Concat(appendedActivity)
            .GroupBy(item => item.EventId, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(item => item.OccurredAt).First())
            .OrderBy(item => item.Sequence)
            .ThenBy(item => item.OccurredAt)
            .TakeLast(100)
            .ToArray();
        return (Map(run), activity);
    }

    public async ValueTask<DeploymentStatusResponse> GetDeploymentAsync(
        string billerId,
        string deploymentId,
        CancellationToken cancellationToken) =>
        Map(await repository.GetDeploymentAsync(billerId, deploymentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Deployment '{deploymentId}' was not found for biller '{billerId}'."));

    /// <summary>
    /// Published artifacts and public reads are keyed by slug, so two billers must never
    /// share one. Appends -2, -3, … until free.
    /// </summary>
    private async ValueTask<string> ReserveSlugAsync(string requested, CancellationToken cancellationToken)
    {
        var candidate = requested;
        for (var suffix = 2; await repository.SlugExistsAsync(candidate, cancellationToken); suffix++)
        {
            candidate = SuffixSlug(requested, suffix);
            if (!SlugRegex().IsMatch(candidate))
            {
                LogValidationError(logger, null, "slug", "Slug cannot be auto-deduplicated.");
                throw new ArgumentException(
                    $"Slug '{requested}' cannot be auto-deduplicated within the 63-character limit.");
            }
        }

        return candidate;
    }

    /// <summary>
    /// Appends -2, -3, … while keeping the result DNS-safe: the base is truncated so the
    /// total stays within 63 characters, and a hyphen exposed by the cut is trimmed so the
    /// suffix never produces a double hyphen or a dangling one.
    /// </summary>
    private static string SuffixSlug(string baseSlug, int suffix)
    {
        var tail = $"-{suffix}";
        var maxBaseLength = 63 - tail.Length;
        var trimmedBase = baseSlug.Length <= maxBaseLength
            ? baseSlug
            : baseSlug[..maxBaseLength].TrimEnd('-');
        return $"{trimmedBase}{tail}";
    }

    private async ValueTask<BillerRecord> GetRequiredBillerAsync(string billerId, CancellationToken cancellationToken) =>
        await repository.GetBillerAsync(billerId, cancellationToken)
        ?? throw new KeyNotFoundException($"Biller '{billerId}' was not found.");

    private async ValueTask<ExperienceRecord> GetRequiredExperienceAsync(string billerId, CancellationToken cancellationToken) =>
        await repository.GetLatestExperienceAsync(billerId, cancellationToken)
        ?? throw new KeyNotFoundException($"No experience exists for biller '{billerId}'.");

    private async ValueTask<OnboardingRunRecord> GetRequiredRunAsync(string billerId, CancellationToken cancellationToken) =>
        await repository.GetRunAsync(billerId, RunId, cancellationToken)
        ?? throw new KeyNotFoundException($"No onboarding session exists for biller '{billerId}'.");

    private void ValidateCreateRequest(CreateBillerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Length > 160)
        {
            LogValidationError(logger, null, "display_name", "Display name is required and limited to 160 characters.");
            throw new ArgumentException("Display name is required and limited to 160 characters.");
        }
        if (!SlugRegex().IsMatch(request.Slug))
        {
            LogValidationError(logger, null, "slug", "Slug must be DNS-safe.");
            throw new ArgumentException("Slug must contain 3 to 63 lowercase letters, numbers, or hyphens.");
        }
        if (!PostalCodeRegex().IsMatch(request.PostalCode))
        {
            LogValidationError(logger, null, "postal_code", "Postal code must contain five digits.");
            throw new ArgumentException("Postal code must contain five digits.");
        }
        if (string.IsNullOrWhiteSpace(request.BillType))
        {
            LogValidationError(logger, null, "bill_type", "Bill type is required.");
            throw new ArgumentException("Bill type is required.");
        }
    }

    private static BillerExperienceDefinition CreateInitialDefinition(BillerRecord biller)
    {
        var primary = biller.Brand?.PrimaryColor ?? "#085368";
        var secondary = biller.Brand?.SecondaryColor ?? "#18B4E9";
        var root = biller.Website ?? new Uri($"https://{biller.Slug}.example.invalid");
        var capabilities = biller.PaymentRails.Count == 0
            ? new[] { "card", "ach" }
            : biller.PaymentRails.Select(rail => rail.Capability).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return new BillerExperienceDefinition(
            "1.1",
            biller.Id,
            new ExperienceBrand(biller.Name, primary, secondary, biller.Brand?.LogoAssetId, biller.Brand?.FontFamily ?? "Inter"),
            new ExperienceContent(
                $"Pay your {biller.BillType.ToLowerInvariant()} bill",
                $"A simple, secure way to pay {biller.Name}.",
                biller.Support is null ? $"Contact {biller.Name} for support." : $"Questions? Contact {biller.Support.Email}.",
                new Uri(root, "/privacy"),
                new Uri(root, "/terms")),
            new PwaConfiguration(biller.Name, biller.Name[..Math.Min(12, biller.Name.Length)], primary, "#FFFFFF", null),
            capabilities,
            new ExperienceUi(
                "centered-card",
                new ExperienceTheme("comfortable", "rounded", "subtle"),
                [
                    new("account", "account-summary"),
                    new("amount", "amount-due", "prominent"),
                    new("methods", "payment-methods"),
                    new("support", "support", "compact")
                ],
                [new("primary-payment-action", "Pay Now", ExperienceActionType.StartPayment)]),
            new ExperiencePreferences(
                GuestCheckoutAllowed: true,
                OfferAutopay: true,
                EnrollDuringPayment: true,
                OfferPaperless: true,
                ReminderChannel.Both,
                capabilities,
                SelfServiceHistory: true,
                SelfServiceUpdates: true,
                FeeHandling.Mixed,
                new PreviewPreferences("desktop", ["payment", "history", "communication", "complex"]),
                new Dictionary<string, string>
                {
                    ["guest_checkout_allowed"] = "Guest checkout reduces friction for one-time payers.",
                    ["offer_autopay"] = "AutoPay gives returning payers a convenient recurring option.",
                    ["offer_paperless"] = "Paperless billing can be offered independently at checkout."
                }));
    }

    private static BillerResponse Map(BillerRecord record) =>
        new(record.Id, record.Name, record.Slug, record.BillType, record.PostalCode, record.Website, record.Brand, record.Support, record.PaymentRails, record.Status, record.CreatedAt);

    private static OnboardingSessionResponse Map(OnboardingRunRecord record) =>
        new(record.Id, record.BillerId, record.State, record.MissingFields, record.UpdatedAt);

    private static ExperienceRevisionResponse Map(ExperienceRecord record) =>
        new(record.BillerId, record.Id, record.Definition, record.State, record.CreatedAt, record.ApprovedAt, record.ETag, record.Findings);

    private static DeploymentStatusResponse Map(DeploymentRecord record) =>
        new(record.Id, record.BillerId, $"config-{record.ConfigVersion}", ParseDeploymentState(record.Status),
            record.PublishedUrl, record.FailureCode, record.FailureMessage, record.UpdatedAt ?? record.RequestedAt);

    private static DeploymentState ParseDeploymentState(string state) => state switch
    {
        "requested" => DeploymentState.Requested,
        "applying" => DeploymentState.Applying,
        "waiting_for_readiness" => DeploymentState.WaitingForReadiness,
        "verifying" => DeploymentState.Verifying,
        "ready" => DeploymentState.Ready,
        "failed" => DeploymentState.Failed,
        "rolled_back" => DeploymentState.RolledBack,
        _ => DeploymentState.Failed
    };

    private static ComplianceReviewService CreateDefaultComplianceService()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new BillerExperienceOptions());
        return new ComplianceReviewService(
            new CompliancePolicyEngine(options),
            options,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ComplianceReviewService>.Instance);
    }

    private static Activity? StartActivity(string name) => BillerExperienceTelemetry.Source.StartActivity(name);

    // W3C traceparent (00-{trace_id}-{span_id}-{flags}) captured at publish-enqueue so the Worker
    // can link its asynchronous processing span back to the originating API request.
    internal static string? FormatTraceparent(Activity? activity)
    {
        if (activity is null)
        {
            return null;
        }

        var context = activity.Context;
        if (context.TraceId == default || context.SpanId == default)
        {
            return null;
        }

        var flags = (context.TraceFlags & ActivityTraceFlags.Recorded) != 0 ? "01" : "00";
        return $"00-{context.TraceId}-{context.SpanId}-{flags}";
    }

    private static void RecordTransition(OnboardingSessionState from, OnboardingSessionState to) =>
        BillerExperienceTelemetry.StateTransitions.Add(1, new("from", from.ToString()), new("to", to.ToString()));

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{1,61}[a-z0-9])$")]
    private static partial Regex SlugRegex();

    [GeneratedRegex("^[0-9]{5}$")]
    private static partial Regex PostalCodeRegex();

    [LoggerMessage(1000, LogLevel.Information, "Created biller {BillerId}, session {SessionId}, model provider {Provider}")]
    private static partial void LogBillerCreated(ILogger logger, string billerId, string sessionId, string provider);

    [LoggerMessage(1001, LogLevel.Information, "Completed chat turn {Step} for biller {BillerId}, session {SessionId}; state {State}, provider {Provider}")]
    private static partial void LogChatCompleted(ILogger logger, string billerId, string sessionId, int step, OnboardingSessionState state, string provider);

    [LoggerMessage(1002, LogLevel.Information, "Updated draft version {Version} for biller {BillerId}; {FindingCount} findings")]
    private static partial void LogDraftUpdated(ILogger logger, string billerId, int version, int findingCount);

    [LoggerMessage(1003, LogLevel.Information, "Approved experience {Revision} for biller {BillerId} by {ApprovedBy}")]
    private static partial void LogExperienceApproved(ILogger logger, string billerId, string revision, string approvedBy);

    [LoggerMessage(1004, LogLevel.Information, "Requested publication of revision {Revision} for biller {BillerId}; deployment {DeploymentId}")]
    private static partial void LogPublicationRequested(ILogger logger, string billerId, string revision, string deploymentId);

    [LoggerMessage(1005, LogLevel.Information, "Reused publication of revision {Revision} for biller {BillerId}; deployment {DeploymentId}")]
    private static partial void LogDuplicatePublication(ILogger logger, string billerId, string revision, string deploymentId);

    [LoggerMessage(1900, LogLevel.Error, "Validation failed for biller {BillerId}, field {Field}: {Reason}")]
    private static partial void LogValidationError(ILogger logger, string? billerId, string field, string reason);

    [LoggerMessage(1902, LogLevel.Error, "Invoice seeding failed for biller {BillerId}; continuing with biller creation")]
    private static partial void LogInvoiceSeedingFailed(ILogger logger, string billerId, Exception exception);

    [LoggerMessage(1903, LogLevel.Error, "Approval blocked for biller {BillerId}, revision {Revision}; finding {FindingCode}: {FindingMessage}")]
    private static partial void LogBlockingFinding(
        ILogger logger,
        string billerId,
        string revision,
        string findingCode,
        string findingMessage);
}
