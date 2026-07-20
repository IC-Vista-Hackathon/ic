using System.Globalization;
using Pronto.BillerExperience.Contracts.V1.Compliance;
using Pronto.BillerExperience.Contracts.V1.Experiences;

namespace Pronto.BillerExperience.Api.Application.Compliance.Checkers;

/// <summary>
/// Hard WCAG AA color-contrast checker over the brand palette. The primary brand color is the
/// experience's foreground for headings and call-to-action buttons, so it must clear the AA
/// normal-text ratio (4.5:1) against the payer surface background. Deterministic contrast math —
/// this makes the accessibility concern a gate, not an advisory suggestion.
/// </summary>
public sealed class ColorContrastChecker : IComplianceChecker
{
    public const string Id = "color_contrast";

    public string CheckerId => Id;
    public bool IsHard => true;

    public ComplianceCheckResult Check(ComplianceCheckContext context)
    {
        var primary = context.Definition.Brand.PrimaryColor;
        var background = context.Definition.Pwa.BackgroundColor;

        if (!WcagContrast.TryParseHex(primary, out var primaryRgb) ||
            !WcagContrast.TryParseHex(background, out var backgroundRgb))
        {
            return ComplianceCheckResults.Failed(
                CheckerId,
                context.PolicyVersion,
                new ComplianceFinding(
                    "BRAND_CONTRAST_UNVERIFIABLE",
                    "Brand and background colors must be six-digit hex values so color contrast can be certified.",
                    ComplianceFindingSeverity.Blocking,
                    RequiresReview: false,
                    FieldPath: "brand.primary_color",
                    PolicyVersion: context.PolicyVersion));
        }

        var ratio = WcagContrast.Ratio(primaryRgb, backgroundRgb);
        if (ratio >= WcagContrast.AaNormalText)
        {
            return ComplianceCheckResults.Passed(CheckerId, context.PolicyVersion);
        }

        return ComplianceCheckResults.Failed(
            CheckerId,
            context.PolicyVersion,
            new ComplianceFinding(
                "BRAND_CONTRAST_INSUFFICIENT",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Primary brand color {0} on background {1} has a contrast ratio of {2:0.00}:1, below the WCAG AA minimum of {3:0.0}:1.",
                    primary,
                    background,
                    ratio,
                    WcagContrast.AaNormalText),
                ComplianceFindingSeverity.Blocking,
                RequiresReview: false,
                FieldPath: "brand.primary_color",
                PolicyVersion: context.PolicyVersion));
    }
}
