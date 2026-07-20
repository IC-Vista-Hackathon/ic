using Pronto.BillerExperience.Contracts.V1.Compliance;
using Pronto.BillerExperience.Contracts.V1.Experiences;

namespace Pronto.BillerExperience.Api.Application.Compliance.Checkers;

/// <summary>
/// Certifies that every payment method the experience accepts is available for the biller's
/// operating jurisdiction, resolved deterministically from its ZIP code. Fails when the jurisdiction
/// cannot be established (cannot certify) or when an accepted method is not permitted there.
/// </summary>
public sealed class PaymentMethodJurisdictionChecker(JurisdictionPaymentMethodPolicy policy) : IComplianceChecker
{
    public const string Id = "payment_method_jurisdiction";

    public string CheckerId => Id;
    public bool IsHard => true;

    public ComplianceCheckResult Check(ComplianceCheckContext context)
    {
        var stateCode = UsPostalJurisdictionResolver.ResolveStateCode(context.Biller.PostalCode);
        if (stateCode is null)
        {
            return ComplianceCheckResults.Failed(
                CheckerId,
                context.PolicyVersion,
                new ComplianceFinding(
                    "PAYMENT_METHOD_JURISDICTION_UNKNOWN",
                    $"The operating jurisdiction could not be established from postal code '{context.Biller.PostalCode}', so payment-method availability cannot be certified.",
                    ComplianceFindingSeverity.Blocking,
                    RequiresReview: false,
                    FieldPath: "biller.postal_code",
                    PolicyVersion: context.PolicyVersion));
        }

        // Accepted methods (payer-facing) union enabled capabilities, so an unavailable rail is caught
        // regardless of which surface enabled it.
        var methods = (context.Definition.Preferences?.AcceptedMethods ?? [])
            .Concat(context.Definition.EnabledPaymentCapabilities)
            .Select(JurisdictionPaymentMethodPolicy.Normalize)
            .Where(method => method.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var unavailable = methods
            .Where(method => !policy.IsPermitted(stateCode, method))
            .ToArray();

        if (unavailable.Length == 0)
        {
            return ComplianceCheckResults.Passed(CheckerId, context.PolicyVersion);
        }

        return ComplianceCheckResults.Failed(
            CheckerId,
            context.PolicyVersion,
            new ComplianceFinding(
                "PAYMENT_METHOD_NOT_PERMITTED_IN_JURISDICTION",
                $"Payment methods not available for jurisdiction {stateCode}: {string.Join(", ", unavailable)}.",
                ComplianceFindingSeverity.Blocking,
                RequiresReview: false,
                FieldPath: "preferences.accepted_methods",
                Jurisdiction: stateCode,
                PolicyVersion: context.PolicyVersion));
    }
}
