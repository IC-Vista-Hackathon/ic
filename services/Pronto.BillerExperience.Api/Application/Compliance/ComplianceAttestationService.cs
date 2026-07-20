using Pronto.BillerExperience.Api.Application.Compliance.Checkers;
using Pronto.BillerExperience.Api.Configuration;
using Pronto.BillerExperience.Api.Domain;
using Pronto.BillerExperience.Contracts.V1.Compliance;
using Pronto.BillerExperience.Contracts.V1.Experiences;
using Microsoft.Extensions.Options;

namespace Pronto.BillerExperience.Api.Application.Compliance;

public interface IComplianceAttestationService
{
    /// <summary>
    /// Runs the deterministic checker suite over the exact revision and returns a signed attestation.
    /// <see cref="ComplianceAttestation.Passed"/> is true only when every hard checker passed.
    /// </summary>
    ComplianceAttestation Attest(
        BillerRecord biller,
        BillerExperienceDefinition definition,
        string revision,
        int configVersion);

    /// <summary>Findings from failing hard checkers — the set that must block publish.</summary>
    IReadOnlyList<ComplianceFinding> GatingFindings(ComplianceAttestation attestation);

    /// <summary>Verifies the attestation's signature against the configuration it certifies.</summary>
    bool Verify(ComplianceAttestation attestation, BillerExperienceDefinition definition);
}

/// <summary>
/// Aggregates the deterministic compliance checkers into a single signed, auditable attestation per
/// configuration revision. This is the certifying gate for publish: only these deterministic checkers
/// decide pass/fail, while the LLM reviewer stays advisory.
/// </summary>
public sealed class ComplianceAttestationService : IComplianceAttestationService
{
    private readonly IReadOnlyList<IComplianceChecker> _checkers;
    private readonly HashSet<string> _hardCheckerIds;
    private readonly ComplianceAttestationSigner _signer;
    private readonly string _policyVersion;
    private readonly TimeProvider _timeProvider;

    public ComplianceAttestationService(
        IEnumerable<IComplianceChecker> checkers,
        ComplianceAttestationSigner signer,
        IOptions<BillerExperienceOptions> options,
        TimeProvider? timeProvider = null)
    {
        _checkers = checkers.ToArray();
        _hardCheckerIds = _checkers.Where(checker => checker.IsHard).Select(checker => checker.CheckerId).ToHashSet(StringComparer.Ordinal);
        _signer = signer;
        _policyVersion = options.Value.Compliance.PolicyVersion;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ComplianceAttestation Attest(
        BillerRecord biller,
        BillerExperienceDefinition definition,
        string revision,
        int configVersion)
    {
        var context = new ComplianceCheckContext(biller, definition, _policyVersion);
        var results = _checkers.Select(checker => checker.Check(context)).ToArray();
        var passed = _checkers.All(checker =>
            !checker.IsHard ||
            results.First(result => string.Equals(result.CheckerId, checker.CheckerId, StringComparison.Ordinal)).Passed);

        var configHash = _signer.ComputeConfigHash(definition);
        var resultsHash = _signer.ComputeResultsHash(results);
        var signature = _signer.Sign(biller.Id, revision, configVersion, _policyVersion, passed, configHash, resultsHash);

        return new ComplianceAttestation(
            biller.Id,
            revision,
            configVersion,
            _policyVersion,
            passed,
            results,
            configHash,
            resultsHash,
            signature,
            ComplianceAttestationSigner.Algorithm,
            _timeProvider.GetUtcNow());
    }

    public IReadOnlyList<ComplianceFinding> GatingFindings(ComplianceAttestation attestation) =>
        attestation.Results
            .Where(result => !result.Passed && _hardCheckerIds.Contains(result.CheckerId))
            .SelectMany(result => result.Findings)
            .ToArray();

    public bool Verify(ComplianceAttestation attestation, BillerExperienceDefinition definition) =>
        _signer.Verify(attestation, definition);
}
