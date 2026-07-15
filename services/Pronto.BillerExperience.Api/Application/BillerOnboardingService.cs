using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Text.Json;
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
using Pronto.BillerExperience.Contracts.V1.Billing;
using Pronto.BillerExperience.Contracts.V1.AgentContext;
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
    IComplianceReviewService? complianceReviewService = null,
    BillingDiscoveryEngine? billingDiscovery = null)
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
        var biller = await ReserveSlugAndCreateBillerAsync(id, normalizedSlug, request, now, cancellationToken);
        var definition = CreateInitialDefinition(biller);
        var experience = new ExperienceRecord(
            "config-1",
            id,
            1,
            ExperienceRevisionState.Draft,
            definition,
            Array.Empty<ComplianceFinding>(),
            now);
        var discovery = billingDiscovery?.Inspect(BillingProfile.Empty);
        var run = new OnboardingRunRecord(
            RunId,
            id,
            "biller-onboarding",
            OnboardingSessionState.CollectingInformation,
            0,
            [new OnboardingChatMessage("assistant", discovery is null
                ? $"Welcome! I created a starting preview for {biller.Name}. Tell me what you want customers to feel or change."
                : $"Welcome! I created a starting preview for {biller.Name}. {discovery.CurrentQuestion!.Prompt}", now)],
            discovery?.MissingFields ?? ["review_brand", "review_legal_links", "review_payment_methods"],
            now,
            discovery?.Profile);

        try
        {
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
        catch (Exception exception) when (!IsCriticalException(exception))
        {
            await CleanupFailedCreationAsync(id);
            throw;
        }
    }

    public async ValueTask<BillerResponse> GetBillerAsync(string billerId, CancellationToken cancellationToken)
    {
        var biller = await GetRequiredBillerAsync(billerId, cancellationToken);
        return Map(biller);
    }

    public async ValueTask<BillerResponse> AdvancePurchaseAsync(
        string billerId,
        AdvanceBillerPurchaseRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PurchaseId))
        {
            throw new ArgumentException("purchase_id is required.", nameof(request));
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var current = await GetRequiredBillerAsync(billerId, cancellationToken);
            if (current.Status is BillerStatus.Purchased or BillerStatus.Live)
            {
                if (current.PurchaseId == request.PurchaseId && current.Tier == request.Tier)
                {
                    return Map(current);
                }

                if (current.PurchaseId is null && current.Tier == request.Tier)
                {
                    var adopted = current with { PurchaseId = request.PurchaseId };
                    try
                    {
                        return Map(await repository.SaveBillerAsync(
                            adopted,
                            current.ETag,
                            cancellationToken));
                    }
                    catch (ConcurrencyException) when (attempt < 2)
                    {
                        continue;
                    }
                }

                throw new BillerPurchaseConflictException(
                    $"Biller '{billerId}' already has a different completed purchase.");
            }

            if (current.Status is not (BillerStatus.Prospect or BillerStatus.Demo))
            {
                throw new BillerPurchaseConflictException(
                    $"Biller '{billerId}' cannot be purchased from status '{current.Status}'.");
            }

            var purchased = current with
            {
                Status = BillerStatus.Purchased,
                Tier = request.Tier,
                PurchaseId = request.PurchaseId
            };

            try
            {
                return Map(await repository.SaveBillerAsync(
                    purchased,
                    current.ETag,
                    cancellationToken));
            }
            catch (ConcurrencyException) when (attempt < 2)
            {
            }
        }

        throw new ConcurrencyException("The biller purchase could not be committed after three attempts.");
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
        if (experience.State != ExperienceRevisionState.Draft)
        {
            LogValidationError(logger, billerId, "state", "Only draft experiences can be changed through chat.");
            throw new ArgumentException("Only draft experiences can be changed through chat.");
        }

        var userMessage = new OnboardingChatMessage("user", request.Message.Trim(), DateTimeOffset.UtcNow);
        var messages = run.Messages.Append(userMessage).ToArray();
        BillingDiscoveryTurn? discoveryTurn = null;
        if (billingDiscovery is not null)
        {
            if (request.BillingAnswers is { Count: > 0 })
            {
                if (request.BillingAnswers.Count > 4)
                {
                    LogValidationError(logger, billerId, "billing_answers", "At most four guided billing answers are accepted per request.");
                    throw new ArgumentException("At most four guided billing answers are accepted per request.", nameof(request));
                }

                var profile = run.BillingProfile;
                foreach (var answer in request.BillingAnswers)
                {
                    if (string.IsNullOrWhiteSpace(answer.Answer) || answer.Answer.Length > 4000)
                    {
                        LogValidationError(logger, billerId, "billing_answers", "Every guided billing answer must contain 1 to 4000 characters.");
                        throw new ArgumentException("Every guided billing answer must contain 1 to 4000 characters.", nameof(request));
                    }

                    var expected = billingDiscovery.Inspect(profile).CurrentQuestion;
                    if (expected is null || expected.Dimension != answer.Dimension)
                    {
                        var expectedName = expected?.Dimension.ToString() ?? "none";
                        LogValidationError(logger, billerId, "billing_answers", $"Expected {expectedName}, received {answer.Dimension}.");
                        throw new ArgumentException($"The guided billing answers are stale or out of order. Expected {expectedName}.", nameof(request));
                    }

                    // A guided client asks one question per dimension, but the server expands the
                    // question graph per billing category. Fan the single answer across the sibling
                    // per-category questions of the same dimension so multi-category billers stay in
                    // sync instead of desyncing the fixed client answer stream.
                    var answerText = answer.Answer.Trim();
                    discoveryTurn = billingDiscovery.ApplyAnswer(billerId, profile, answerText);
                    profile = discoveryTurn.State.Profile;
                    if (!discoveryTurn.AnswerAccepted) break;
                    while (discoveryTurn.State.CurrentQuestion?.Dimension == answer.Dimension)
                    {
                        var sibling = billingDiscovery.ApplyAnswer(billerId, profile, answerText);
                        if (!sibling.AnswerAccepted) break;
                        discoveryTurn = sibling;
                        profile = discoveryTurn.State.Profile;
                    }
                }
            }
            else
            {
                discoveryTurn = billingDiscovery.ApplyAnswer(billerId, run.BillingProfile, request.Message.Trim());
            }
        }
        var effectiveBillingProfile = discoveryTurn?.State.Profile ?? run.BillingProfile ?? BillingProfile.Empty;
        activity?.SetTag("ic.billing.category_count", effectiveBillingProfile.Categories.Count);
        activity?.SetTag("ic.billing.discovery_complete", billingDiscovery?.Inspect(effectiveBillingProfile).Progress.IsComplete ?? false);
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
            new BillerExperienceChatWorkflowInput(biller, experience, messages, effectiveBillingProfile, eventSink),
            orchestrationContext,
            cancellationToken);
        var boundedReply = discoveryTurn is null
            ? generated.Reply
            : $"{generated.Reply.Trim()}\n\n{discoveryTurn.Reply}";
        var assistantMessage = new OnboardingChatMessage("assistant", boundedReply, DateTimeOffset.UtcNow);
        var missingFields = discoveryTurn?.State.MissingFields ?? generated.MissingFields;
        var nextState = missingFields.Count == 0
            ? OnboardingSessionState.DraftReady
            : OnboardingSessionState.CollectingInformation;
        var updatedExperience = experience with
        {
            // The draft generator's schema doesn't own the design brief; carry it forward
            // so the bespoke-skin input survives each onboarding turn.
            Definition = generated.Definition with
            {
                BillerId = billerId,
                Brief = generated.Definition.Brief ?? experience.Definition.Brief,
                Billing = BillingProfilePresentation.Project(effectiveBillingProfile)
            },
            Findings = generated.Findings
        };
        var latestRun = await GetRequiredRunAsync(billerId, cancellationToken);
        var updatedRun = latestRun with
        {
            State = nextState,
            Step = run.Step + 1,
            Messages = messages.Append(assistantMessage).ToArray(),
            MissingFields = missingFields,
            BillingProfile = effectiveBillingProfile,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var savedExperience = await repository.SaveExperienceAsync(updatedExperience, experience.ETag, cancellationToken);
        var savedRun = await repository.SaveRunAsync(updatedRun, latestRun.ETag, cancellationToken);
        if (discoveryTurn?.AnswerAccepted == true && agentContextService is not null)
        {
            await ShareBillingProfileAsync(billerId, savedRun.Id, discoveryTurn.State.Profile, cancellationToken);
        }
        BillerExperienceTelemetry.ChatTurns.Add(1, new KeyValuePair<string, object?>("provider", draftGenerator.Provider));
        RecordTransition(run.State, nextState);
        LogChatCompleted(logger, billerId, savedRun.Id, savedRun.Step, nextState, draftGenerator.Provider);
        return new OnboardingChatResponse(boundedReply, Map(savedRun), Map(savedExperience), generated.GenerationMode);
    }

    private async ValueTask ShareBillingProfileAsync(
        string billerId,
        string runId,
        BillingProfile profile,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await agentContextService!.GetAsync(billerId, runId, cancellationToken);
            await agentContextService.AppendAsync(billerId, runId, new AppendAgentContextRequest(
                snapshot.Version,
                profile.Confirmed ? AgentContextEntryKind.AcceptedArtifact : AgentContextEntryKind.CandidateArtifact,
                "onboarding-agent",
                "billing_discovery",
                JsonSerializer.Serialize(profile),
                []), cancellationToken);
        }
        catch (Exception exception)
        {
            // The typed run document remains authoritative. Context sharing is supplementary and
            // must not erase a biller's accepted answer when an MCP consumer is unavailable.
            LogBillingContextShareFailed(logger, billerId, runId, exception);
        }
    }

    public async ValueTask<OnboardingSessionResponse> ReopenBillingQuestionAsync(
        string billerId,
        ReopenBillingQuestionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.QuestionId))
        {
            LogValidationError(logger, billerId, "question_id", "Question id is required.");
            throw new ArgumentException("question_id is required.", nameof(request));
        }

        var run = await GetRequiredRunAsync(billerId, cancellationToken);
        if (run.State is OnboardingSessionState.Approved or OnboardingSessionState.Publishing or OnboardingSessionState.Published)
        {
            LogValidationError(logger, billerId, "state", "Approved or published billing profiles cannot be reopened through onboarding chat.");
            throw new ArgumentException("Only a draft billing profile can be edited.");
        }

        if (billingDiscovery is null)
        {
            throw new InvalidOperationException("Billing discovery is not configured.");
        }
        var reopened = billingDiscovery.Reopen(billerId, run.BillingProfile, request.QuestionId);
        var saved = await repository.SaveRunAsync(run with
        {
            State = OnboardingSessionState.CollectingInformation,
            MissingFields = reopened.MissingFields,
            BillingProfile = reopened.Profile,
            UpdatedAt = DateTimeOffset.UtcNow
        }, run.ETag, cancellationToken);
        return Map(saved);
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
        var definition = request.Definition with
        {
            BillerId = billerId,
            Brief = request.Definition.Brief ?? current.Definition.Brief
        };
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

        var discovery = billingDiscovery?.Inspect(run.BillingProfile);
        if (discovery is not null && !discovery.Progress.IsComplete)
        {
            var finding = new ComplianceFinding(
                "BILLING_DISCOVERY_INCOMPLETE",
                $"Complete the billing interview before approval. Next required item: {discovery.CurrentQuestion?.Prompt}",
                ComplianceFindingSeverity.Blocking);
            LogBlockingFinding(logger, billerId, experience.Id, finding.Code, finding.Message);
            throw new ExperienceValidationException(
                "The billing policy is incomplete. Continue the chat and confirm category-specific cadence, state rules, and payment terms.",
                [finding]);
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
        try
        {
            await repository.SaveRunAsync(
                run with { State = OnboardingSessionState.Approved, MissingFields = Array.Empty<string>(), UpdatedAt = now },
                run.ETag,
                cancellationToken);
        }
        catch (Exception)
        {
            // The two writes span separate documents (and Cosmos containers), so they can't share a
            // transaction. Compensate the already-approved experience back to its prior revision
            // state so we never leave an experience Approved while its run is not.
            await CompensateExperienceAsync(experience, saved.ETag);
            throw;
        }
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
        var now = DateTimeOffset.UtcNow;
        var existing = await repository.GetDeploymentAsync(billerId, deploymentId, cancellationToken);
        IReadOnlyList<ComplianceFinding>? publishFindings = null;
        var requeuingFailedPublication =
            experience.State == ExperienceRevisionState.Failed &&
            existing?.Status == "failed";
        if (experience.State == ExperienceRevisionState.Approved || requeuingFailedPublication)
        {
            publishFindings = await _compliance.ReviewAsync(
                biller,
                experience.Definition,
                ComplianceReviewStage.Publish,
                cancellationToken);
            var blockingFindings = publishFindings
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
        }

        DeploymentRecord deployment;
        if (existing is not null)
        {
            LogDuplicatePublication(logger, billerId, request.Revision, deploymentId);
            deployment = existing;
            if (existing.Status == "failed")
            {
                try
                {
                    deployment = await repository.SaveDeploymentAsync(
                        existing with
                        {
                            Status = "requested",
                            UpdatedAt = now,
                            PublishedUrl = null,
                            FailureCode = null,
                            FailureMessage = null,
                            Traceparent = FormatTraceparent(Activity.Current),
                            ClaimedAt = null,
                            LeaseExpiresAt = null
                        },
                        existing.ETag,
                        cancellationToken);
                }
                catch (ConcurrencyException)
                {
                    var concurrent = await repository.GetDeploymentAsync(billerId, deploymentId, cancellationToken);
                    if (concurrent is null || concurrent.Status == "failed")
                    {
                        throw;
                    }

                    deployment = concurrent;
                }
                LogPublicationRequested(logger, billerId, experience.Id, deployment.Id);
            }
        }
        else
        {
            if (experience.State != ExperienceRevisionState.Approved)
            {
                LogValidationError(logger, billerId, "state", "The current revision must be approved before publication.");
                throw new ArgumentException("The current revision must be approved before publication.");
            }

            deployment = await repository.CreateDeploymentAsync(
                new DeploymentRecord(deploymentId, billerId, experience.Version, "requested", now,
                    Traceparent: FormatTraceparent(Activity.Current)),
                cancellationToken);
            LogPublicationRequested(logger, billerId, experience.Id, deployment.Id);
        }

        // The deployment record is the durable source of truth for a publication request; advancing
        // the experience/run to Publishing is a separate, idempotent step so a retry after a failure
        // between the writes converges the states to match the deployment instead of leaving them
        // stuck at Approved or Failed. Terminal deployments never regress workflow state.
        var publicationInProgress = deployment.Status is "requested" or "applying" or "verifying";
        if (publicationInProgress &&
            experience.State is ExperienceRevisionState.Approved or ExperienceRevisionState.Failed)
        {
            await repository.SaveExperienceAsync(
                experience with
                {
                    State = ExperienceRevisionState.Publishing,
                    Findings = publishFindings ?? experience.Findings
                },
                experience.ETag,
                cancellationToken);
        }
        if (publicationInProgress &&
            run.State is OnboardingSessionState.Approved or OnboardingSessionState.Failed)
        {
            await repository.SaveRunAsync(run with { State = OnboardingSessionState.Publishing, UpdatedAt = now }, run.ETag, cancellationToken);
            RecordTransition(run.State, OnboardingSessionState.Publishing);
        }

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
    /// Best-effort rollback of an experience write when a follow-on write in the same logical
    /// transition fails. Restores <paramref name="original"/> using the ETag produced by the write
    /// being rolled back. Runs with <see cref="CancellationToken.None"/> because the triggering
    /// failure may be the caller's cancellation — a cancelled rollback would leave the exact
    /// inconsistency it exists to prevent. A failed rollback is logged, not thrown, so the original
    /// failure surfaces.
    /// </summary>
    private async ValueTask CompensateExperienceAsync(ExperienceRecord original, string? currentETag)
    {
        try
        {
            await repository.SaveExperienceAsync(original, currentETag, CancellationToken.None);
        }
        catch (Exception rollbackFailure)
        {
            LogCompensationFailed(logger, original.BillerId, original.Id, rollbackFailure);
        }
    }

    /// <summary>
    /// Picks a free slug and creates the biller under it atomically. The repository's
    /// <see cref="IBillerExperienceRepository.CreateBillerAsync"/> is the race-free gate: if a
    /// concurrent creation reserved the same slug between our availability check and the create,
    /// it throws <see cref="SlugConflictException"/> and we re-derive the next free slug and retry.
    /// </summary>
    private async ValueTask<BillerRecord> ReserveSlugAndCreateBillerAsync(
        string id,
        string normalizedSlug,
        CreateBillerRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Each losing racer re-derives the next free suffix, so under N-way contention a single
        // request can lose at most N-1 times; the bound is generous relative to realistic
        // same-slug concurrency and only guards against a pathological non-terminating loop.
        const int maxAttempts = 64;
        for (var attempt = 1; ; attempt++)
        {
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
            try
            {
                await repository.CreateBillerAsync(biller, cancellationToken);
                return biller;
            }
            catch (SlugConflictException) when (attempt < maxAttempts)
            {
                // Another creation claimed this slug first; loop to pick the next free one.
            }
        }
    }

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

    private async ValueTask CleanupFailedCreationAsync(string billerId)
    {
        try
        {
            await repository.PurgeByBillerAsync(billerId, CancellationToken.None);
        }
        catch (Exception exception) when (!IsCriticalException(exception))
        {
            LogCreationCleanupFailed(logger, billerId, "purge", exception);
        }
    }

    private static bool IsCriticalException(Exception exception) =>
        exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            or AppDomainUnloadedException
            or BadImageFormatException
            or CannotUnloadAppDomainException
            or InvalidProgramException;

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
                }),
            CreateInitialBrief(biller, root));
    }

    private static DesignBrief CreateInitialBrief(BillerRecord biller, Uri root)
    {
        var keywords = new List<string> { "trustworthy", "secure", "straightforward", biller.BillType.ToLowerInvariant() };
        if (biller.BillType.Contains("tax", StringComparison.OrdinalIgnoreCase)
            || biller.BillType.Contains("utility", StringComparison.OrdinalIgnoreCase)
            || biller.Name.Contains("city", StringComparison.OrdinalIgnoreCase)
            || biller.Name.Contains("county", StringComparison.OrdinalIgnoreCase))
        {
            keywords.Add("civic");
            keywords.Add("community");
        }

        var assets = new List<BrandAsset>();
        if (biller.Brand?.LogoAssetId is { Length: > 0 } logo)
        {
            assets.Add(new BrandAsset("logo", new Uri(logo, UriKind.RelativeOrAbsolute), $"{biller.Name} logo"));
        }

        return new DesignBrief(
            VoiceAndTone: "Reassuring, plain-language, and efficient. Confident without jargon.",
            VisualStyle: "Modern civic: generous whitespace, calm surfaces, clear hierarchy, accessible contrast.",
            BrandKeywords: keywords,
            Assets: assets,
            ReferenceUrl: biller.Website ?? root);
    }

    private static BillerResponse Map(BillerRecord record) =>
        new(record.Id, record.Name, record.Slug, record.BillType, record.PostalCode, record.Website, record.Brand, record.Support, record.PaymentRails, record.Status, record.CreatedAt, record.Tier);

    private OnboardingSessionResponse Map(OnboardingRunRecord record)
    {
        var discovery = billingDiscovery?.Inspect(record.BillingProfile);
        return new(record.Id, record.BillerId, record.State, discovery?.MissingFields ?? record.MissingFields, record.UpdatedAt,
            discovery?.Profile, discovery?.CurrentQuestion, discovery?.Progress);
    }

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
    [LoggerMessage(1904, LogLevel.Error, "Compensating approval rollback failed for biller {BillerId}, revision {Revision}; state may be inconsistent")]
    private static partial void LogCompensationFailed(
        ILogger logger,
        string billerId,
        string revision,
        Exception exception);

    [LoggerMessage(1905, LogLevel.Error, "Creation cleanup operation {Operation} failed for biller {BillerId}")]
    private static partial void LogCreationCleanupFailed(
        ILogger logger,
        string billerId,
        string operation,
        Exception exception);

    [LoggerMessage(1906, LogLevel.Error, "Sharing the billing profile through agent context failed for biller {BillerId}, run {RunId}; the typed profile remains persisted")]
    private static partial void LogBillingContextShareFailed(
        ILogger logger,
        string billerId,
        string runId,
        Exception exception);
}

public sealed class BillerPurchaseConflictException(string message) : Exception(message);
