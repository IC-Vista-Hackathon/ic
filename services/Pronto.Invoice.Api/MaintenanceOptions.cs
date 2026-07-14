namespace Pronto.Invoice.Api;

/// <summary>
/// Gates the test-data maintenance endpoint. Off by default; set true only in nonprod
/// (via <c>Maintenance__PurgeEnabled</c>) so functional tests can clean up after themselves.
/// </summary>
public sealed class MaintenanceOptions
{
    public const string SectionName = "Maintenance";

    public bool PurgeEnabled { get; init; }
}
