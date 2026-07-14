using System.Diagnostics;
using System.Text.RegularExpressions;
using IC.BillerExperience.Api.Domain;
using IC.BillerExperience.Api.Infrastructure;
using IC.BillerExperience.Api.Infrastructure.AI;
using IC.BillerExperience.Api.Infrastructure.Persistence;
using IC.BillerExperience.Contracts.V1.Billers;
using IC.BillerExperience.Contracts.V1.Deployments;
using IC.BillerExperience.Contracts.V1.Experiences;
using IC.BillerExperience.Contracts.V1.Onboarding;

namespace IC.BillerExperience.Api.Application;

public sealed partial class BillerOnboardingService(
    IBillerExperienceRepository repository,
    IExperienceDraftGenerator draftGenerator,
    ILogger<BillerOnboardingService> logger)
{
    private const string RunId = "onboarding";

    public async ValueTask<(BillerResponse Biller, OnboardingSessionResponse Session, ExperienceRevisionResponse Draft)> CreateAsync(
        CreateBillerRequest request,
        CancellationToken cancellationToken)
    {
        using var activity = StartActivity("biller.create");
        ValidateCreateRequest(request);
        var id = Guid.NewGuid().ToString("N");
        activity?.SetTag("ic.biller_id", id);
        var now = DateTimeOffset.UtcNow;
        var biller = new BillerRecord(
            id,
            request.DisplayName.Trim(),
            request.Slug.Trim().ToLowerInvariant(),
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
        var savedExperience = await repository.SaveExperienceAsync(experience, null, cancellationToken);
        var savedRun = await repository.SaveRunAsync(run, null, cancellationToken);
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
        var generated = await draftGenerator.GenerateAsync(biller, experience, messages, cancellationToken);
        var assistantMessage = new OnboardingChatMessage("assistant", generated.Reply, DateTimeOffset.UtcNow);
        var nextState = generated.MissingFields.Count == 0
            ? OnboardingSessionState.DraftReady
            : OnboardingSessionState.CollectingInformation;
        var updatedExperience = experience with
        {
            Definition = generated.Definition with { BillerId = billerId },
            Findings = generated.Findings
        };
        var updatedRun = run with
        {
            State = nextState,
            Step = run.Step + 1,
            Messages = messages.Append(assistantMessage).ToArray(),
            MissingFields = generated.MissingFields,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var savedExperience = await repository.SaveExperienceAsync(updatedExperience, experience.ETag, cancellationToken);
        var savedRun = await repository.SaveRunAsync(updatedRun, run.ETag, cancellationToken);
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

        var findings = ValidateDefinition(billerId, request.Definition);
        var saved = await repository.SaveExperienceAsync(
            current with { Definition = request.Definition with { BillerId = billerId }, Findings = findings },
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
        var run = await GetRequiredRunAsync(billerId, cancellationToken);
        if (experience.Id != request.Revision)
        {
            LogValidationError(logger, billerId, "revision", "The requested revision is not current.");
            throw new ArgumentException("The requested revision is not current.");
        }

        var findings = ValidateDefinition(billerId, experience.Definition);
        if (findings.Any(finding => finding.Severity == ComplianceFindingSeverity.Blocking))
        {
            LogValidationError(logger, billerId, "compliance", "Blocking validation findings must be resolved.");
            throw new ArgumentException("Blocking validation findings must be resolved before approval.");
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

        var now = DateTimeOffset.UtcNow;
        var deployment = await repository.CreateDeploymentAsync(
            new DeploymentRecord(deploymentId, billerId, experience.Version, "requested", now),
            cancellationToken);
        await repository.SaveExperienceAsync(experience with { State = ExperienceRevisionState.Publishing }, experience.ETag, cancellationToken);
        await repository.SaveRunAsync(run with { State = OnboardingSessionState.Publishing, UpdatedAt = now }, run.ETag, cancellationToken);
        RecordTransition(run.State, OnboardingSessionState.Publishing);
        LogPublicationRequested(logger, billerId, experience.Id, deployment.Id);
        return Map(deployment);
    }

    public async ValueTask<OnboardingSessionResponse> GetSessionAsync(string billerId, CancellationToken cancellationToken) =>
        Map(await GetRequiredRunAsync(billerId, cancellationToken));

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

    private List<ComplianceFinding> ValidateDefinition(string billerId, BillerExperienceDefinition definition)
    {
        var findings = new List<ComplianceFinding>();
        if (!HexColorRegex().IsMatch(definition.Brand.PrimaryColor) || !HexColorRegex().IsMatch(definition.Brand.SecondaryColor))
        {
            findings.Add(new("BRAND_COLOR_INVALID", "Brand colors must use six-digit hexadecimal values.", ComplianceFindingSeverity.Blocking));
        }
        if (definition.EnabledPaymentCapabilities.Count == 0)
        {
            findings.Add(new("PAYMENT_METHOD_REQUIRED", "At least one existing payment capability is required.", ComplianceFindingSeverity.Blocking));
        }
        findings.Add(new("COMPLIANCE_REVIEW_REQUIRED", "Compliance guidance must be reviewed by the biller before publication.", ComplianceFindingSeverity.Warning));
        if (findings.Any(finding => finding.Severity == ComplianceFindingSeverity.Blocking))
        {
            BillerExperienceTelemetry.ValidationFailures.Add(1, new KeyValuePair<string, object?>("scope", "experience"));
            LogDefinitionValidationFailed(logger, billerId, findings.Count);
        }
        return findings;
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
            "1.0",
            biller.Id,
            new ExperienceBrand(biller.Name, primary, secondary, biller.Brand?.LogoAssetId, biller.Brand?.FontFamily ?? "Inter"),
            new ExperienceContent(
                $"Pay your {biller.BillType.ToLowerInvariant()} bill",
                $"A simple, secure way to pay {biller.Name}.",
                biller.Support is null ? $"Contact {biller.Name} for support." : $"Questions? Contact {biller.Support.Email}.",
                new Uri(root, "/privacy"),
                new Uri(root, "/terms")),
            new PwaConfiguration(biller.Name, biller.Name[..Math.Min(12, biller.Name.Length)], primary, "#FFFFFF", null),
            capabilities);
    }

    private static BillerResponse Map(BillerRecord record) =>
        new(record.Id, record.Name, record.Slug, record.BillType, record.PostalCode, record.Website, record.Brand, record.Support, record.PaymentRails, record.Status, record.CreatedAt);

    private static OnboardingSessionResponse Map(OnboardingRunRecord record) =>
        new(record.Id, record.BillerId, record.State, record.MissingFields, record.UpdatedAt);

    private static ExperienceRevisionResponse Map(ExperienceRecord record) =>
        new(record.BillerId, record.Id, record.Definition, record.State, record.CreatedAt, record.ApprovedAt, record.ETag, record.Findings);

    private static DeploymentStatusResponse Map(DeploymentRecord record) =>
        new(record.Id, record.BillerId, $"config-{record.ConfigVersion}", DeploymentState.Requested, null, null, null, record.RequestedAt);

    private static Activity? StartActivity(string name) => BillerExperienceTelemetry.Source.StartActivity(name);

    private static void RecordTransition(OnboardingSessionState from, OnboardingSessionState to) =>
        BillerExperienceTelemetry.StateTransitions.Add(1, new("from", from.ToString()), new("to", to.ToString()));

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{1,61}[a-z0-9])$")]
    private static partial Regex SlugRegex();

    [GeneratedRegex("^[0-9]{5}$")]
    private static partial Regex PostalCodeRegex();

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColorRegex();

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

    [LoggerMessage(1901, LogLevel.Error, "Experience validation failed for biller {BillerId} with {FindingCount} findings")]
    private static partial void LogDefinitionValidationFailed(ILogger logger, string billerId, int findingCount);
}
