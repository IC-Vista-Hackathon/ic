using Pronto.BillerExperience.Contracts.V1.Billing;
using Pronto.BillerExperience.Contracts.V1.Experiences;

namespace Pronto.BillerExperience.Api.Application;

internal static class BillingProfilePresentation
{
    public static BillingPresentation Project(BillingProfile profile) => new(
        profile.Categories.Select(category => new BillingPresentationCategory(
            category.Id,
            category.DisplayName,
            category.Cadence?.Kind,
            DescribeCadence(category.Cadence),
            DescribeState(category.StateRules),
            category.PaymentTerms?.Mode,
            category.PaymentTerms?.MaximumInstallments)).ToArray());

    private static string DescribeCadence(BillingCadence? cadence) => cadence?.Kind switch
    {
        BillingCadenceKind.Monthly => "Monthly",
        BillingCadenceKind.Quarterly => "Quarterly",
        BillingCadenceKind.Annual => "Annually",
        BillingCadenceKind.OneTime => "One-time",
        BillingCadenceKind.AdHoc => "As needed",
        BillingCadenceKind.Custom => cadence.Details?.Trim() is { Length: > 0 } details ? details : "Custom",
        _ => "Not set"
    };

    private static string DescribeState(IReadOnlyList<AccountStateRule>? rules) =>
        rules is { Count: > 0 }
            ? string.Join(" ", rules.Select(rule => rule.Description.Trim()))
            : "Not set";
}
