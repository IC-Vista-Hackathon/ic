using Pronto.BillerExperience.Api.Domain;
using Pronto.BillerExperience.Contracts.V1.Compliance;
using Pronto.BillerExperience.Contracts.V1.Experiences;

namespace Pronto.BillerExperience.Api.Application.Compliance.Checkers;

/// <summary>
/// Immutable input a deterministic checker evaluates. Bundles the biller (for jurisdiction) and the
/// exact configuration revision under review, plus the policy version stamped on every finding.
/// </summary>
public sealed record ComplianceCheckContext(
    BillerRecord Biller,
    BillerExperienceDefinition Definition,
    string PolicyVersion);

/// <summary>
/// A single deterministic compliance checker. Checkers are pure functions of the configuration: no
/// I/O, no model calls, no randomness. Only checkers CERTIFY publish; the LLM reviewer stays
/// advisory. A checker is "hard" when a failing result must block publication.
/// </summary>
public interface IComplianceChecker
{
    /// <summary>Stable identifier recorded in the attestation (snake_case).</summary>
    string CheckerId { get; }

    /// <summary>
    /// Whether a failing result blocks publish. All checkers in the F8 suite are hard by design;
    /// the flag keeps the aggregation explicit and lets advisory checkers be added later.
    /// </summary>
    bool IsHard { get; }

    ComplianceCheckResult Check(ComplianceCheckContext context);
}
