namespace Pronto.BillerExperience.Contracts.V1.Preview;

/// <summary>
/// Descriptor of a provisioned Studio preview tenant. The preview renders the same built payer PWA
/// as production, but scoped to an isolated <see cref="PreviewBillerId"/> partition seeded with
/// synthetic data — so the biller previews exactly what ships without touching any live tenant.
/// </summary>
/// <param name="BillerId">The live biller the preview shadows.</param>
/// <param name="PreviewBillerId">Isolated, preview-flagged tenant id the PWA + services are scoped to.</param>
/// <param name="AccountNumber">Seeded demo account the preview's bill lookup resolves.</param>
/// <param name="ConfigPath">
/// API-relative path serving the current draft config for the preview tenant, so the built bundle
/// renders the in-progress experience (not stale published or client-sample data).
/// </param>
public sealed record PreviewTenantResponse(
    string BillerId,
    string PreviewBillerId,
    string AccountNumber,
    string ConfigPath);
