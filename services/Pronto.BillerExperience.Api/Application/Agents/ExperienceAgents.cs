using Pronto.BillerExperience.Api.Domain;
using Pronto.BillerExperience.Api.Application.Compliance;
using Pronto.BillerExperience.Api.Infrastructure.AI;
using Pronto.BillerExperience.Contracts.V1.Experiences;
using Pronto.BillerExperience.Contracts.V1.Billing;
using Pronto.BillerExperience.Contracts.V1.Onboarding;
using Pronto.BillerExperience.Contracts.V1.Research;

namespace Pronto.BillerExperience.Api.Application.Agents;

/// <summary>
/// Bounded agent contracts. Agents perform domain work; orchestration decides when they run,
/// supplies context, and moves their typed results toward the workflow goal.
/// </summary>
internal interface IExperienceDesignAgent
{
    ValueTask<DraftGenerationResult> DesignAsync(
        BillerRecord biller,
        ExperienceRecord experience,
        IReadOnlyList<OnboardingChatMessage> messages,
        BillingProfile billingProfile,
        BillerResearchResponse research,
        CancellationToken cancellationToken);
}

internal interface IAccessibilityReviewAgent
{
    ValueTask<IReadOnlyList<ComplianceFinding>> ReviewAsync(
        BillerExperienceDefinition definition,
        CancellationToken cancellationToken);
}

internal interface IComplianceReviewAgent
{
    ValueTask<IReadOnlyList<ComplianceFinding>> ReviewAsync(
        BillerRecord biller,
        BillerExperienceDefinition definition,
        CancellationToken cancellationToken);
}

internal sealed class ExperienceDesignAgent(IExperienceDraftGenerator generator) : IExperienceDesignAgent
{
    public ValueTask<DraftGenerationResult> DesignAsync(
        BillerRecord biller,
        ExperienceRecord experience,
        IReadOnlyList<OnboardingChatMessage> messages,
        BillingProfile billingProfile,
        BillerResearchResponse research,
        CancellationToken cancellationToken) =>
        generator.GenerateAsync(biller, experience, messages, billingProfile, research, cancellationToken);
}

internal sealed class AccessibilityReviewAgent : IAccessibilityReviewAgent
{
    public ValueTask<IReadOnlyList<ComplianceFinding>> ReviewAsync(
        BillerExperienceDefinition definition,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var findings = new List<ComplianceFinding>();
        if (TryRelativeLuminance(definition.Brand.PrimaryColor, out var luminance) &&
            ContrastRatio(luminance, 1.0) < 4.5)
        {
            findings.Add(new ComplianceFinding(
                "PRIMARY_ACTION_CONTRAST",
                "The primary brand color does not provide 4.5:1 contrast against white text; choose a darker action color.",
                ComplianceFindingSeverity.Blocking));
        }

        if ((definition.Ui?.Actions ?? []).Any(action => string.IsNullOrWhiteSpace(action.Label)))
        {
            findings.Add(new ComplianceFinding(
                "ACTION_ACCESSIBLE_NAME_REQUIRED",
                "Every payment action needs a visible accessible label.",
                ComplianceFindingSeverity.Blocking));
        }

        return ValueTask.FromResult<IReadOnlyList<ComplianceFinding>>(findings);
    }

    private static bool TryRelativeLuminance(string color, out double luminance)
    {
        luminance = 0;
        if (color.Length != 7 || color[0] != '#' ||
            !byte.TryParse(color.AsSpan(1, 2), System.Globalization.NumberStyles.HexNumber, null, out var red) ||
            !byte.TryParse(color.AsSpan(3, 2), System.Globalization.NumberStyles.HexNumber, null, out var green) ||
            !byte.TryParse(color.AsSpan(5, 2), System.Globalization.NumberStyles.HexNumber, null, out var blue))
            return false;

        static double Linearize(byte channel)
        {
            var value = channel / 255d;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        luminance = 0.2126 * Linearize(red) + 0.7152 * Linearize(green) + 0.0722 * Linearize(blue);
        return true;
    }

    private static double ContrastRatio(double first, double second) =>
        (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
}

internal sealed class ComplianceReviewAgent(IComplianceReviewService compliance) : IComplianceReviewAgent
{
    public ValueTask<IReadOnlyList<ComplianceFinding>> ReviewAsync(
        BillerRecord biller,
        BillerExperienceDefinition definition,
        CancellationToken cancellationToken) =>
        compliance.ReviewAsync(biller, definition, ComplianceReviewStage.Draft, cancellationToken);
}
