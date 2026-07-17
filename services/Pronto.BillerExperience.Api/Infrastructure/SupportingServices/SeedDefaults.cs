namespace Pronto.BillerExperience.Api.Infrastructure.SupportingServices;

/// <summary>
/// Shared constants for the onboarding seed path so the invoice seeder and the payer seeder agree
/// on the demo account number: invoices are seeded under it and the demo payer is linked to it, so
/// the live payer site's account lookup resolves matching invoice + payer SERVICE data.
/// </summary>
public static class SeedDefaults
{
    /// <summary>
    /// The demo account number every seeded biller's invoices and demo payer attach to. Matches the
    /// account the payer site/preview defaults to (<c>4421</c>).
    /// </summary>
    public const string PreviewAccountNumber = "4421";
}
