using System.Diagnostics;
using Pronto.BillerExperience.Api.Infrastructure;
using Pronto.BillerExperience.Api.Infrastructure.SupportingServices;
using Pronto.BillerExperience.Contracts.V1.Experiences;
using Pronto.BillerExperience.Contracts.V1.Preview;
using Pronto.ServiceDefaults;

namespace Pronto.BillerExperience.Api.Application.Preview;

/// <summary>
/// Provisions and resets the isolated, seeded, resettable Studio preview tenant behind F2.
///
/// The Studio preview runs the SAME built payer PWA against the REAL Invoice/Payment/PayerAccount
/// services — the drift-free path — but scoped to a <see cref="PreviewTenant"/> partition
/// (<c>preview-{billerId}</c>) seeded with synthetic demo data linked to the shared preview account.
/// That keeps the preview separate from any live/published tenant, and lets the Payment Service flag
/// preview settlements (real state on the fake rail) so they're excluded from genuine reporting.
///
/// Seeding reuses the existing <see cref="IInvoiceSeeder"/> (the FR-6 onboarding seeder) rather than
/// duplicating it, so provisioning and reset produce the same category-relevant demo invoices the
/// rest of the platform seeds — just addressed to the preview tenant.
/// </summary>
public sealed partial class PreviewProvisioningService(
    BillerOnboardingService onboarding,
    IInvoiceSeeder invoiceSeeder,
    ILogger<PreviewProvisioningService> logger)
{
    /// <summary>Shared demo account the preview's bill lookup resolves (matches the invoice seeder).</summary>
    public const string PreviewAccountNumber = "4421";

    /// <summary>
    /// Ensure a preview tenant exists and is seeded for <paramref name="billerId"/>. Idempotent:
    /// re-invoking refreshes the seed for the same preview partition.
    /// </summary>
    public ValueTask<PreviewTenantResponse> ProvisionAsync(string billerId, CancellationToken cancellationToken) =>
        SeedPreviewAsync(billerId, "preview.provision", cancellationToken);

    /// <summary>
    /// Wipe + re-seed the preview tenant deterministically, so a "Restart preview" produces a fresh,
    /// repeatable demo. Seeding runs in replace mode, so the Invoice service clears the isolated
    /// preview partition before re-seeding — a re-seed overwrites rather than accumulating.
    /// </summary>
    public ValueTask<PreviewTenantResponse> ResetAsync(string billerId, CancellationToken cancellationToken) =>
        SeedPreviewAsync(billerId, "preview.reset", cancellationToken);

    private async ValueTask<PreviewTenantResponse> SeedPreviewAsync(
        string billerId, string activityName, CancellationToken cancellationToken)
    {
        // Validates existence — throws NotFound if the biller is unknown, before we touch services.
        var biller = await onboarding.GetBillerAsync(billerId, cancellationToken);
        var previewBillerId = PreviewTenant.ForBiller(biller.BillerId);

        using var activity = BillerExperienceTelemetry.Source.StartActivity(activityName);
        activity?.SetTag("ic.biller_id", biller.BillerId);
        activity?.SetTag("ic.preview_biller_id", previewBillerId);

        // Seed the isolated preview partition with the same demo invoices the platform seeds, keyed
        // to the preview tenant rather than the live biller. Replace mode makes provision/reset
        // deterministic — the preview partition is cleared first, so repeats don't accumulate.
        await invoiceSeeder.SeedAsync(
            new SeedBillerContext(previewBillerId, biller.DisplayName, biller.BillType, biller.Website),
            cancellationToken,
            replace: true);

        LogPreviewSeeded(logger, biller.BillerId, previewBillerId, activityName, Activity.Current?.TraceId.ToString());
        return new PreviewTenantResponse(
            BillerId: biller.BillerId,
            PreviewBillerId: previewBillerId,
            AccountNumber: PreviewAccountNumber,
            ConfigPath: PreviewConfigPath(previewBillerId));
    }

    /// <summary>
    /// The current draft experience definition for a preview tenant, with its <c>biller_id</c>
    /// rewritten to the preview partition. The built PWA loads this so the preview reflects exactly
    /// the in-progress config, while its service calls target the isolated, seeded preview tenant.
    /// </summary>
    public async ValueTask<BillerExperienceDefinition> ResolvePreviewConfigAsync(
        string previewBillerId, CancellationToken cancellationToken)
    {
        var liveBillerId = PreviewTenant.LiveBillerId(previewBillerId);
        var draft = await onboarding.GetDraftAsync(liveBillerId, cancellationToken);
        return draft.Definition with { BillerId = PreviewTenant.ForBiller(previewBillerId) };
    }

    /// <summary>API-relative path serving the preview tenant's current draft config for the PWA.</summary>
    public static string PreviewConfigPath(string previewBillerId) =>
        $"/public/experiences/preview/{Uri.EscapeDataString(previewBillerId)}";

    [LoggerMessage(1500, LogLevel.Information,
        "Seeded preview tenant {PreviewBillerId} for biller {BillerId} via {Operation}; trace {TraceId}")]
    private static partial void LogPreviewSeeded(
        ILogger logger, string billerId, string previewBillerId, string operation, string? traceId);
}
