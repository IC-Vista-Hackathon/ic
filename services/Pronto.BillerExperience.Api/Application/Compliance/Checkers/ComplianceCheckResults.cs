using Pronto.BillerExperience.Contracts.V1.Compliance;
using Pronto.BillerExperience.Contracts.V1.Experiences;

namespace Pronto.BillerExperience.Api.Application.Compliance.Checkers;

/// <summary>Factory helpers for the two shapes a deterministic checker returns.</summary>
public static class ComplianceCheckResults
{
    public static ComplianceCheckResult Passed(string checkerId, string policyVersion) =>
        new(checkerId, true, [], policyVersion);

    public static ComplianceCheckResult Failed(
        string checkerId,
        string policyVersion,
        params ComplianceFinding[] findings) =>
        new(checkerId, false, findings, policyVersion);
}
