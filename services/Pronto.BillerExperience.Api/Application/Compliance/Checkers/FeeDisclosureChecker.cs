using Pronto.BillerExperience.Contracts.V1.Compliance;
using Pronto.BillerExperience.Contracts.V1.Experiences;

namespace Pronto.BillerExperience.Api.Application.Compliance.Checkers;

/// <summary>
/// Certifies that a fee disclosure is present whenever fees are passed to the payer. When the biller
/// charges or splits fees (<see cref="FeeHandling.Charge"/> / <see cref="FeeHandling.Mixed"/>), the
/// configuration must carry a non-empty <see cref="ExperienceContent.FeeDisclosure"/>. When the
/// biller absorbs fees, no disclosure is required.
/// </summary>
public sealed class FeeDisclosureChecker : IComplianceChecker
{
    public const string Id = "fee_disclosure";
    private const int MinimumDisclosureLength = 12;

    public string CheckerId => Id;
    public bool IsHard => true;

    public ComplianceCheckResult Check(ComplianceCheckContext context)
    {
        var feeHandling = context.Definition.Preferences?.FeeHandling ?? FeeHandling.Undecided;
        var passesFeesToPayer = feeHandling is FeeHandling.Charge or FeeHandling.Mixed;
        if (!passesFeesToPayer)
        {
            return ComplianceCheckResults.Passed(CheckerId, context.PolicyVersion);
        }

        var disclosure = context.Definition.Content.FeeDisclosure;
        if (!string.IsNullOrWhiteSpace(disclosure) && disclosure.Trim().Length >= MinimumDisclosureLength)
        {
            return ComplianceCheckResults.Passed(CheckerId, context.PolicyVersion);
        }

        return ComplianceCheckResults.Failed(
            CheckerId,
            context.PolicyVersion,
            new ComplianceFinding(
                "FEE_DISCLOSURE_REQUIRED",
                "Fees are passed to the payer, so a clear fee disclosure must be present in the experience content.",
                ComplianceFindingSeverity.Blocking,
                RequiresReview: false,
                FieldPath: "content.fee_disclosure",
                PolicyVersion: context.PolicyVersion));
    }
}
