using Pronto.BillerExperience.Contracts.V1.Compliance;
using Pronto.BillerExperience.Contracts.V1.Experiences;

namespace Pronto.BillerExperience.Api.Application.Compliance.Checkers;

/// <summary>
/// Certifies the PII-not-logged posture: the configured telemetry policy must not enroll any field
/// whose name denotes personally identifiable information. Mirrors the payer PWA's structural PII
/// exclusion (<c>telemetryPolicy.ts</c>) into a deterministic, server-side gate. No telemetry
/// policy (the default) is the safe posture and passes.
/// </summary>
public sealed class TelemetryPiiChecker : IComplianceChecker
{
    public const string Id = "telemetry_pii";

    public string CheckerId => Id;
    public bool IsHard => true;

    // Substrings that mark a captured field as PII. Matched case-insensitively against the
    // normalized field name, so "payer_email", "customerEmail", and "email" all trip the gate.
    private static readonly string[] PiiMarkers =
    [
        "email", "phone", "name", "address", "street", "city", "zip", "postal",
        "ssn", "social_security", "dob", "birth", "account_number", "accountnumber",
        "card", "pan", "cvv", "routing", "iban", "tax_id", "taxid", "license",
        "passport", "ip_address", "ipaddress", "geolocation", "latitude", "longitude",
    ];

    public ComplianceCheckResult Check(ComplianceCheckContext context)
    {
        var telemetry = context.Definition.Telemetry;
        var capturedFields = telemetry?.CapturedFields ?? [];
        if (capturedFields.Count == 0)
        {
            return ComplianceCheckResults.Passed(CheckerId, context.PolicyVersion);
        }

        var offending = capturedFields
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .Where(IsPii)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (offending.Length == 0)
        {
            return ComplianceCheckResults.Passed(CheckerId, context.PolicyVersion);
        }

        return ComplianceCheckResults.Failed(
            CheckerId,
            context.PolicyVersion,
            new ComplianceFinding(
                "TELEMETRY_PII_CONFIGURED",
                $"Telemetry policy captures fields that denote PII and must not be logged: {string.Join(", ", offending)}.",
                ComplianceFindingSeverity.Blocking,
                RequiresReview: false,
                FieldPath: "telemetry.captured_fields",
                PolicyVersion: context.PolicyVersion));
    }

    private static bool IsPii(string field)
    {
        var normalized = field.Trim().ToLowerInvariant();
        return PiiMarkers.Any(marker => normalized.Contains(marker, StringComparison.Ordinal));
    }
}
