namespace Pronto.BillerExperience.Contracts.V1.Branding;

/// <summary>A request to read a biller's public website and infer its brand.</summary>
public sealed record BrandScanRequest(Uri Website);

/// <summary>
/// Brand assets inferred from a biller's website. Colors are 6-digit lowercase hex,
/// <see cref="FontFamily"/> is the primary named typeface, and <see cref="LogoUrl"/> points at the
/// most logo-like image found (apple-touch-icon / og:image / favicon). Any field may be null when
/// the site does not expose it.
/// </summary>
public sealed record BrandScanResponse(
    BrandScanOutcome Outcome,
    string? PrimaryColor,
    string? SecondaryColor,
    string? AccentColor,
    string? FontFamily,
    Uri? LogoUrl,
    IReadOnlyList<string> Palette,
    IReadOnlyList<string> Warnings,
    string? ErrorCode = null);

public enum BrandScanOutcome
{
    Completed,
    Degraded,
    Failed
}
