using Pronto.BillerExperience.Contracts.V1.Experiences;

namespace Pronto.BillerExperience.Contracts.V1.Compliance;

/// <summary>
/// Structured result of a single deterministic compliance checker over a configuration revision.
/// Only deterministic checkers produce these; the LLM reviewer remains advisory and never certifies.
/// </summary>
public sealed record ComplianceCheckResult(
    string CheckerId,
    bool Passed,
    IReadOnlyList<ComplianceFinding> Findings,
    string PolicyVersion);

/// <summary>
/// Signed, auditable evidence that the deterministic compliance suite ran against an exact
/// configuration revision. The signature covers a hash of the config revision and a hash of the
/// aggregated results, so tampering with either the config or the recorded results invalidates it.
/// Persisted with the deployment/publication record so every published revision has verifiable
/// evidence of the gate it passed.
/// </summary>
public sealed record ComplianceAttestation(
    string BillerId,
    string Revision,
    int ConfigVersion,
    string PolicyVersion,
    bool Passed,
    IReadOnlyList<ComplianceCheckResult> Results,
    string ConfigHash,
    string ResultsHash,
    string Signature,
    string SignatureAlgorithm,
    DateTimeOffset CreatedAt);
