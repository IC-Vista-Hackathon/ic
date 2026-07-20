namespace Pronto.BillerExperience.Api.Application.Compliance.Checkers;

/// <summary>
/// Canonical ordered set of deterministic compliance checkers. Used both for the DI registration and
/// the in-memory default so the suite is identical wherever it runs.
/// </summary>
public static class ComplianceCheckerCatalog
{
    public static IReadOnlyList<IComplianceChecker> CreateDefault() =>
    [
        new FeeDisclosureChecker(),
        new PaymentMethodJurisdictionChecker(JurisdictionPaymentMethodPolicy.Default),
        new LegalLinksChecker(),
        new ColorContrastChecker(),
        new TelemetryPiiChecker(),
    ];
}
