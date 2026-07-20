using System.Net;
using System.Text.Json.Nodes;
using Xunit;

namespace Pronto.Functional.Tests;

/// <summary>
/// FR-10 — The Studio preview runs the shipped payer bundle against the real services, backed by an
/// isolated, seeded, resettable preview tenant (F2). These deployed tests assert the observable
/// contract over HTTP: provisioning seeds an isolated <c>preview-{billerId}</c> partition, the served
/// preview config is the draft pointed at that partition, and reset is deterministic (re-seeding
/// converges rather than accumulating). See docs/pronto-functional-requirements.md.
/// </summary>
[Trait(Categories.Name, Categories.Functional)]
public sealed class StudioPreviewTests
{
    [SkippableFact]
    public async Task ProvisioningSeedsAnIsolatedPreviewTenant()
    {
        ProntoEnvironment.RequireBaseUrl();
        using var client = new ProntoApiClient();

        var billerId = await CreateBillerAsync(client);

        var preview = await client.ProvisionPreviewAsync(billerId);
        var previewBillerId = preview["preview_biller_id"].AsStringOrNull();
        var accountNumber = preview["account_number"].AsStringOrNull();

        Assert.Equal($"preview-{billerId}", previewBillerId);
        Assert.NotEqual(billerId, previewBillerId);
        Assert.False(string.IsNullOrWhiteSpace(accountNumber), "preview must expose a seeded demo account");

        var invoices = await client.GetInvoicesAsync(previewBillerId!, accountNumber!);
        Assert.NotEmpty(invoices);
    }

    [SkippableFact]
    public async Task PreviewConfigServesTheDraftScopedToThePreviewTenant()
    {
        ProntoEnvironment.RequireBaseUrl();
        using var client = new ProntoApiClient();

        var billerId = await CreateBillerAsync(client);
        var preview = await client.ProvisionPreviewAsync(billerId);
        var previewBillerId = preview["preview_biller_id"]!.GetValue<string>();

        using var response = await client.GetPreviewConfigAsync(previewBillerId);
        var payload = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode is HttpStatusCode.OK,
            $"expected 200 for the preview config but got {(int)response.StatusCode}: {payload}");

        var definition = JsonNode.Parse(payload)!;
        // The served config must point every downstream service call at the isolated preview
        // partition, so its biller_id is the preview tenant — not the live biller.
        Assert.Equal(previewBillerId, definition["biller_id"].AsStringOrNull());
    }

    [SkippableFact]
    public async Task ResetIsDeterministicAndDoesNotAccumulateSeedData()
    {
        ProntoEnvironment.RequireBaseUrl();
        using var client = new ProntoApiClient();

        var billerId = await CreateBillerAsync(client);
        var preview = await client.ProvisionPreviewAsync(billerId);
        var previewBillerId = preview["preview_biller_id"]!.GetValue<string>();
        var accountNumber = preview["account_number"]!.GetValue<string>();

        var afterProvision = (await client.GetInvoicesAsync(previewBillerId, accountNumber)).Count;
        Assert.True(afterProvision > 0, "provisioning must seed demo invoices for the preview account");

        await client.ResetPreviewAsync(billerId);
        var afterReset = (await client.GetInvoicesAsync(previewBillerId, accountNumber)).Count;

        Assert.Equal(afterProvision, afterReset);
    }

    private static async Task<string> CreateBillerAsync(ProntoApiClient client)
    {
        var created = await client.CreateBillerAsync(
            "Preview Tenant Co", billType: "utility", website: "https://preview-tenant.example.com");
        return created["biller"]!["biller_id"]!.GetValue<string>();
    }
}
