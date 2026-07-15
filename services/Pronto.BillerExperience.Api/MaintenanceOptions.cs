namespace Pronto.BillerExperience.Api;

/// <summary>
/// Gates the test-data maintenance endpoint. Off by default; set true only in nonprod
/// (via <c>Maintenance__PurgeEnabled</c>) so functional tests can clean up after themselves.
/// </summary>
public sealed class MaintenanceOptions
{
    public const string SectionName = "Maintenance";

    public bool PurgeEnabled { get; init; }

    /// <summary>
    /// Gates the <c>seed_invoices</c> MCP tool. Off by default; set true only in nonprod/demo
    /// (via <c>Maintenance__SeedingEnabled</c>) so agents can seed fake invoices for a demo.
    /// The tool reports "unavailable" otherwise, so seeding is invisible in prod.
    /// </summary>
    public bool SeedingEnabled { get; init; }
}
