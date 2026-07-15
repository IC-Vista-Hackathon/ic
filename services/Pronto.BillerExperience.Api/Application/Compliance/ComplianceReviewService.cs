using System.Diagnostics;
using System.Text.RegularExpressions;
using Pronto.BillerExperience.Api.Configuration;
using Pronto.BillerExperience.Api.Domain;
using Pronto.BillerExperience.Api.Infrastructure;
using Pronto.BillerExperience.Contracts.V1.Experiences;
using Microsoft.Extensions.Options;

namespace Pronto.BillerExperience.Api.Application.Compliance;

public enum ComplianceReviewStage
{
    Draft,
    Approval,
    Publish
}

public sealed record ComplianceKnowledgeReview(
    ComplianceKnowledgeReviewStatus Status,
    string Summary,
    IReadOnlyList<ComplianceFinding> Findings,
    IReadOnlyList<Uri> Sources,
    string? ErrorCode = null,
    bool Retryable = false);

public enum ComplianceKnowledgeReviewStatus
{
    Completed,
    NeedsReview,
    Failed
}

public interface IComplianceKnowledgeReviewer
{
    ValueTask<ComplianceKnowledgeReview> ReviewAsync(
        BillerRecord biller,
        BillerExperienceDefinition definition,
        IReadOnlyList<ComplianceFinding> policyFindings,
        ComplianceReviewStage stage,
        CancellationToken cancellationToken);
}

public interface IComplianceReviewService
{
    ValueTask<IReadOnlyList<ComplianceFinding>> ReviewAsync(
        BillerRecord biller,
        BillerExperienceDefinition definition,
        ComplianceReviewStage stage,
        CancellationToken cancellationToken);
}

public sealed partial class CompliancePolicyEngine(IOptions<BillerExperienceOptions> options)
{
    private readonly string _policyVersion = options.Value.Compliance.PolicyVersion;

    public IReadOnlyList<ComplianceFinding> Evaluate(BillerExperienceDefinition definition)
    {
        var findings = new List<ComplianceFinding>();
        if (!HexColorRegex().IsMatch(definition.Brand.PrimaryColor) ||
            !HexColorRegex().IsMatch(definition.Brand.SecondaryColor))
        {
            findings.Add(Blocking(
                "BRAND_COLOR_INVALID",
                "Brand colors must use six-digit hexadecimal values.",
                "brand"));
        }

        if (definition.EnabledPaymentCapabilities.Count == 0)
        {
            findings.Add(Blocking(
                "PAYMENT_METHOD_REQUIRED",
                "At least one existing payment capability is required.",
                "enabled_payment_capabilities"));
        }

        if (!IsHttps(definition.Content.PrivacyPolicyUrl))
        {
            findings.Add(Blocking(
                "PRIVACY_POLICY_HTTPS_REQUIRED",
                "The privacy policy must use an absolute HTTPS URL.",
                "content.privacy_policy_url"));
        }

        if (!IsHttps(definition.Content.TermsOfServiceUrl))
        {
            findings.Add(Blocking(
                "TERMS_HTTPS_REQUIRED",
                "The terms of service must use an absolute HTTPS URL.",
                "content.terms_of_service_url"));
        }

        if (definition.Preferences is { } preferences)
        {
            var unsupportedMethods = preferences.AcceptedMethods
                .Except(definition.EnabledPaymentCapabilities, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (unsupportedMethods.Length > 0)
            {
                findings.Add(Blocking(
                    "PAYMENT_METHOD_UNSUPPORTED",
                    $"Selected methods are not supported by the existing rails: {string.Join(", ", unsupportedMethods)}.",
                    "preferences.accepted_methods"));
            }

            if (preferences.AcceptedMethods.Count == 0)
            {
                findings.Add(Blocking(
                    "PAYMENT_METHOD_SELECTION_REQUIRED",
                    "At least one supported payment method must be selected for the payer experience.",
                    "preferences.accepted_methods"));
            }

            if (preferences.FeeHandling == FeeHandling.Undecided)
            {
                findings.Add(Blocking(
                    "FEE_POLICY_REQUIRED",
                    "Fee handling must be decided before publication.",
                    "preferences.fee_handling"));
            }

            if (preferences.OfferAutopay &&
                !preferences.AcceptedMethods.Any(IsRecurringPaymentMethod))
            {
                findings.Add(Blocking(
                    "AUTOPAY_METHOD_REQUIRED",
                    "AutoPay requires an accepted card or ACH payment method.",
                    "preferences.offer_autopay"));
            }
        }

        foreach (var action in definition.Ui?.Actions ?? [])
        {
            if (string.IsNullOrWhiteSpace(action.Label) || action.Label.Length > 48)
            {
                findings.Add(Blocking(
                    "ACTION_LABEL_INVALID",
                    "Action labels must contain 1 to 48 characters.",
                    $"ui.actions[{action.Id}].label"));
            }

            if (!Enum.IsDefined(action.Action))
            {
                findings.Add(Blocking(
                    "ACTION_TYPE_INVALID",
                    "Actions must use a supported action type.",
                    $"ui.actions[{action.Id}].action"));
                continue;
            }

            if (action.Action == ExperienceActionType.SchedulePayment &&
                !definition.EnabledPaymentCapabilities.Any(IsRecurringPaymentMethod))
            {
                findings.Add(Blocking(
                    "SCHEDULE_METHOD_REQUIRED",
                    "Pay later requires an enabled card or ACH payment method.",
                    $"ui.actions[{action.Id}].action"));
            }
        }

        return findings;
    }

    private ComplianceFinding Blocking(string code, string message, string fieldPath) =>
        new(code, message, ComplianceFindingSeverity.Blocking, false, fieldPath, PolicyVersion: _policyVersion);

    private static bool IsHttps(Uri value) =>
        value.IsAbsoluteUri && string.Equals(value.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    private static bool IsRecurringPaymentMethod(string value) =>
        string.Equals(value, "card", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "ach", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexColorRegex();
}

public sealed partial class ComplianceReviewService(
    CompliancePolicyEngine policyEngine,
    IOptions<BillerExperienceOptions> options,
    ILogger<ComplianceReviewService> logger,
    IComplianceKnowledgeReviewer? knowledgeReviewer = null) : IComplianceReviewService
{
    private readonly ComplianceOptions _options = options.Value.Compliance;

    public async ValueTask<IReadOnlyList<ComplianceFinding>> ReviewAsync(
        BillerRecord biller,
        BillerExperienceDefinition definition,
        ComplianceReviewStage stage,
        CancellationToken cancellationToken)
    {
        using var activity = BillerExperienceTelemetry.Source.StartActivity("compliance.review");
        activity?.SetTag("ic.biller_id", biller.Id);
        activity?.SetTag("compliance.stage", stage.ToString());
        activity?.SetTag("compliance.policy.version", _options.PolicyVersion);
        var policyFindings = policyEngine.Evaluate(definition);
        if (policyFindings.Any(finding => finding.Severity == ComplianceFindingSeverity.Blocking))
        {
            BillerExperienceTelemetry.ValidationFailures.Add(
                1,
                new KeyValuePair<string, object?>("scope", "compliance"));
        }
        // Draft editing can happen many times in one Studio session. Grounded Foundry review is
        // reserved for approval and publish gates so transient model quota is not consumed by
        // every autosave; deterministic policy validation still runs on every draft.
        if (stage == ComplianceReviewStage.Draft)
        {
            activity?.SetTag("compliance.knowledge.status", "deferred_to_gate");
            return policyFindings;
        }
        if (knowledgeReviewer is null)
        {
            return Merge(
                policyFindings,
                _options.RequireFoundryEvidence
                    ? [Unavailable("compliance.knowledge_not_configured", false)]
                    : []);
        }

        try
        {
            var review = await knowledgeReviewer.ReviewAsync(
                biller,
                definition,
                policyFindings,
                stage,
                cancellationToken);
            activity?.SetTag("compliance.knowledge.status", review.Status.ToString());
            activity?.SetTag("compliance.source.count", review.Sources.Count);
            if (review.Status == ComplianceKnowledgeReviewStatus.Failed)
            {
                return Merge(
                    policyFindings,
                    _options.RequireFoundryEvidence
                        ? [Unavailable(review.ErrorCode ?? "compliance.knowledge_failed", review.Retryable)]
                        : [AdvisoryUnavailable(review.ErrorCode ?? "compliance.knowledge_failed")]);
            }

            return Merge(policyFindings, review.Findings);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogKnowledgeFailure(logger, biller.Id, stage, Activity.Current?.TraceId.ToString(), exception);
            activity?.SetStatus(ActivityStatusCode.Error, "compliance.knowledge_failed");
            return Merge(
                policyFindings,
                _options.RequireFoundryEvidence
                    ? [Unavailable("compliance.knowledge_failed", true)]
                    : [AdvisoryUnavailable("compliance.knowledge_failed")]);
        }
    }

    private ComplianceFinding Unavailable(string errorCode, bool retryable) =>
        new(
            "COMPLIANCE_KNOWLEDGE_UNAVAILABLE",
            errorCode.EndsWith("foundry_rate_limited", StringComparison.Ordinal)
                ? "Compliance review is temporarily rate-limited by Azure AI Foundry; publication is paused. Wait a moment and try Publish again."
                : $"Compliance knowledge review is unavailable ({errorCode}); publication cannot continue safely." +
                  (retryable ? " Retry after the Foundry service recovers." : string.Empty),
            ComplianceFindingSeverity.Blocking,
            true,
            PolicyVersion: _options.PolicyVersion);

    private ComplianceFinding AdvisoryUnavailable(string errorCode) =>
        new(
            "COMPLIANCE_KNOWLEDGE_DEGRADED",
            $"Compliance knowledge review is unavailable ({errorCode}); complete a manual review before publication.",
            ComplianceFindingSeverity.Warning,
            true,
            PolicyVersion: _options.PolicyVersion);

    private static ComplianceFinding[] Merge(
        IReadOnlyList<ComplianceFinding> policyFindings,
        IReadOnlyList<ComplianceFinding> knowledgeFindings) =>
        policyFindings
            .Concat(knowledgeFindings)
            .GroupBy(finding => finding.Code, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(finding => finding.Severity).First())
            .ToArray();

    [LoggerMessage(2850, LogLevel.Error,
        "Compliance knowledge review failed for biller {BillerId} at {Stage}; trace {TraceId}")]
    private static partial void LogKnowledgeFailure(
        ILogger logger,
        string billerId,
        ComplianceReviewStage stage,
        string? traceId,
        Exception exception);
}
