using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Pronto.BillerExperience.Contracts.V1.Compliance;
using Pronto.BillerExperience.Contracts.V1.Experiences;

namespace Pronto.BillerExperience.Api.Application.Compliance;

/// <summary>
/// Produces and verifies the signature over a compliance attestation. The signature is an
/// HMAC-SHA256 over the biller/revision identity plus a hash of the exact configuration revision and
/// a hash of the aggregated checker results. Because the HMAC covers those hashes and the key is not
/// known to agents or clients, tampering with the config, the recorded results, or the recorded
/// hashes all invalidate verification.
/// </summary>
public sealed class ComplianceAttestationSigner
{
    public const string Algorithm = "HMACSHA256";

    private static readonly JsonSerializerOptions CanonicalJson = new(JsonSerializerDefaults.Web);

    private readonly byte[] _key;

    public ComplianceAttestationSigner(string signingKey)
    {
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            throw new ArgumentException("A compliance attestation signing key is required.", nameof(signingKey));
        }

        _key = Encoding.UTF8.GetBytes(signingKey);
    }

    public string ComputeConfigHash(BillerExperienceDefinition definition) =>
        Sha256Hex(JsonSerializer.SerializeToUtf8Bytes(definition, CanonicalJson));

    public string ComputeResultsHash(IReadOnlyList<ComplianceCheckResult> results) =>
        Sha256Hex(JsonSerializer.SerializeToUtf8Bytes(results, CanonicalJson));

    public string Sign(
        string billerId,
        string revision,
        int configVersion,
        string policyVersion,
        bool passed,
        string configHash,
        string resultsHash)
    {
        var payload = string.Join(
            '\n',
            billerId,
            revision,
            configVersion.ToString(CultureInfo.InvariantCulture),
            policyVersion,
            passed ? "1" : "0",
            configHash,
            resultsHash);
        using var hmac = new HMACSHA256(_key);
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    }

    /// <summary>
    /// Verifies an attestation against the configuration revision it claims to certify. Returns false
    /// if the config or results were tampered with, if the recorded hashes were altered, or if the
    /// signature does not match.
    /// </summary>
    public bool Verify(ComplianceAttestation attestation, BillerExperienceDefinition definition)
    {
        if (!string.Equals(attestation.SignatureAlgorithm, Algorithm, StringComparison.Ordinal))
        {
            return false;
        }

        var configHash = ComputeConfigHash(definition);
        var resultsHash = ComputeResultsHash(attestation.Results);
        if (!string.Equals(attestation.ConfigHash, configHash, StringComparison.Ordinal) ||
            !string.Equals(attestation.ResultsHash, resultsHash, StringComparison.Ordinal))
        {
            return false;
        }

        var expected = Sign(
            attestation.BillerId,
            attestation.Revision,
            attestation.ConfigVersion,
            attestation.PolicyVersion,
            attestation.Passed,
            attestation.ConfigHash,
            attestation.ResultsHash);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(attestation.Signature));
    }

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
