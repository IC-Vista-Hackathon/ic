using Pronto.BillerExperience.Contracts.V1.Billing;

namespace Pronto.BillerExperience.Api.Application;

/// <summary>
/// Server-owned billing discovery questionnaire. Implemented by <see cref="BillingDiscoveryEngine"/>;
/// the seam lets callers (and tests) substitute the state machine — e.g. to exercise
/// <see cref="BillerOnboardingService"/>'s guided answer fan-out in isolation.
/// </summary>
public interface IBillingDiscovery
{
    BillingDiscoveryState Inspect(BillingProfile? source);

    BillingDiscoveryAssumptionTurn ApplyAssumptions(string billerId, BillingProfile? source, string? billType);

    BillingDiscoveryTurn ApplyAnswer(string billerId, BillingProfile? source, string message);

    BillingDiscoveryState Reopen(string billerId, BillingProfile? source, string questionId);
}
