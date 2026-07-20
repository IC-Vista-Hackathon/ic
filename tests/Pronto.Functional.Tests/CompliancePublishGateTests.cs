using System.Net;
using System.Text.Json.Nodes;
using Xunit;

namespace Pronto.Functional.Tests;

/// <summary>
/// FR-8 — Publish is gated by the deterministic compliance suite, which emits a signed, auditable
/// attestation. These deployed tests exercise the two observable outcomes end-to-end over HTTP:
/// a compliant revision publishes and its 202 response carries a verifiable attestation, and a
/// revision that violates a hard checker is blocked at publish with problem-details findings — even
/// though it passed the (advisory) approval review. See docs/pronto-functional-requirements.md.
/// </summary>
[Trait(Categories.Name, Categories.Functional)]
public sealed class CompliancePublishGateTests
{
    [SkippableFact]
    public async Task PublishProducesSignedAttestationForCompliantRevision()
    {
        ProntoEnvironment.RequireBaseUrl();
        using var client = new ProntoApiClient();

        var billerId = await CreateBillerAsync(client);
        var (revision, definition, etag) = await ReadDraftAsync(client, billerId);

        // Guarantee the revision clears every hard checker regardless of the bootstrap brand:
        // high-contrast palette (color_contrast) with a present fee disclosure (fee_disclosure).
        SetBrandContrast(definition, primary: "#0B3D91", background: "#FFFFFF");
        definition["content"]!["fee_disclosure"] =
            "A service fee may apply and is shown before you confirm any payment.";

        await client.UpdateConfigAsync(billerId, definition, etag);
        await client.ApproveConfigAsync(billerId, revision);

        using var response = await client.PublishConfigAsync(billerId, revision);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode is HttpStatusCode.Accepted,
            $"expected 202 Accepted for a compliant revision but got {(int)response.StatusCode}: {body}");

        var deployment = JsonNode.Parse(body)!;
        var attestation = deployment["attestation"];
        Assert.NotNull(attestation);
        Assert.True(
            attestation!["passed"]?.GetValue<bool>() ?? false,
            $"published attestation must record passed=true: {body}");
        Assert.False(
            string.IsNullOrWhiteSpace(attestation["signature"].AsStringOrNull()),
            "published attestation must carry a signature");
        Assert.False(
            string.IsNullOrWhiteSpace(attestation["config_hash"].AsStringOrNull()),
            "published attestation must carry a config hash");

        var results = attestation["results"]?.AsArray() ?? [];
        Assert.NotEmpty(results);
        var contrast = results.FirstOrDefault(r => r?["checker_id"].AsStringOrNull() == "color_contrast");
        Assert.NotNull(contrast);
        Assert.True(
            contrast!["passed"]?.GetValue<bool>() ?? false,
            "the color_contrast checker must pass for the high-contrast palette");
    }

    [SkippableFact]
    public async Task PublishIsBlockedByHardComplianceChecker()
    {
        ProntoEnvironment.RequireBaseUrl();
        using var client = new ProntoApiClient();

        var billerId = await CreateBillerAsync(client);
        var (revision, definition, etag) = await ReadDraftAsync(client, billerId);

        // Valid six-digit hex (so the approval-stage policy engine still accepts it) but far below the
        // WCAG AA contrast minimum: this is caught only by the deterministic publish checker.
        SetBrandContrast(definition, primary: "#FDFDFD", background: "#FFFFFF");

        await client.UpdateConfigAsync(billerId, definition, etag);
        await client.ApproveConfigAsync(billerId, revision);

        using var response = await client.PublishConfigAsync(billerId, revision);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode is HttpStatusCode.UnprocessableEntity,
            $"expected 422 for a non-contrasting palette but got {(int)response.StatusCode}: {body}");

        var problem = JsonNode.Parse(body)!;
        Assert.Equal("experience_validation_blocked", problem["code"].AsStringOrNull());

        var findings = problem["findings"]?.AsArray() ?? [];
        Assert.Contains(
            findings,
            finding => finding?["field_path"].AsStringOrNull() == "brand.primary_color"
                && (finding["code"].AsStringOrNull()?.StartsWith("BRAND_CONTRAST", StringComparison.Ordinal) ?? false));
    }

    private static async Task<string> CreateBillerAsync(ProntoApiClient client)
    {
        // Postal 10001 (NY) resolves a jurisdiction whose fake rails permit card + ach, so the
        // jurisdiction checker is not the variable under test here.
        var created = await client.CreateBillerAsync(
            "Compliance Gate Co", billType: "utility", website: "https://compliance-gate.example.com",
            postalCode: "10001");
        return created["biller"]!["biller_id"]!.GetValue<string>();
    }

    private static async Task<(string Revision, JsonNode Definition, string? ETag)> ReadDraftAsync(
        ProntoApiClient client, string billerId)
    {
        var config = await client.GetConfigAsync(billerId);
        var revision = config["revision"]!.GetValue<string>();
        var etag = config["etag"].AsStringOrNull();
        var definition = config["definition"]!;
        return (revision, definition, etag);
    }

    private static void SetBrandContrast(JsonNode definition, string primary, string background)
    {
        definition["brand"]!["primary_color"] = primary;
        definition["brand"]!["secondary_color"] = "#123456";
        definition["pwa"]!["background_color"] = background;
    }
}
