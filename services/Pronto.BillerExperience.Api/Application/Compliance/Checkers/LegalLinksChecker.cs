using Pronto.BillerExperience.Contracts.V1.Compliance;
using Pronto.BillerExperience.Contracts.V1.Experiences;

namespace Pronto.BillerExperience.Api.Application.Compliance.Checkers;

/// <summary>
/// Certifies that the required legal surface is present: terms of service and privacy policy links
/// (absolute HTTPS) and a refund/dispute policy link (absolute HTTPS). Each missing or non-HTTPS
/// link is reported as its own finding so the Studio can point the biller at the exact field.
/// </summary>
public sealed class LegalLinksChecker : IComplianceChecker
{
    public const string Id = "legal_links";

    public string CheckerId => Id;
    public bool IsHard => true;

    public ComplianceCheckResult Check(ComplianceCheckContext context)
    {
        var content = context.Definition.Content;
        var findings = new List<ComplianceFinding>();

        AddIfMissing(
            findings,
            content.TermsOfServiceUrl,
            "LEGAL_TERMS_LINK_REQUIRED",
            "A terms of service link (absolute HTTPS URL) is required.",
            "content.terms_of_service_url",
            context.PolicyVersion);
        AddIfMissing(
            findings,
            content.PrivacyPolicyUrl,
            "LEGAL_PRIVACY_LINK_REQUIRED",
            "A privacy policy link (absolute HTTPS URL) is required.",
            "content.privacy_policy_url",
            context.PolicyVersion);
        AddIfMissing(
            findings,
            content.RefundPolicyUrl,
            "LEGAL_REFUND_POLICY_REQUIRED",
            "A refund/dispute policy link (absolute HTTPS URL) is required.",
            "content.refund_policy_url",
            context.PolicyVersion);

        return findings.Count == 0
            ? ComplianceCheckResults.Passed(CheckerId, context.PolicyVersion)
            : ComplianceCheckResults.Failed(CheckerId, context.PolicyVersion, [.. findings]);
    }

    private static void AddIfMissing(
        List<ComplianceFinding> findings,
        Uri? value,
        string code,
        string message,
        string fieldPath,
        string policyVersion)
    {
        if (IsHttps(value))
        {
            return;
        }

        findings.Add(new ComplianceFinding(
            code,
            message,
            ComplianceFindingSeverity.Blocking,
            RequiresReview: false,
            FieldPath: fieldPath,
            PolicyVersion: policyVersion));
    }

    private static bool IsHttps(Uri? value) =>
        value is not null &&
        value.IsAbsoluteUri &&
        string.Equals(value.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
}
