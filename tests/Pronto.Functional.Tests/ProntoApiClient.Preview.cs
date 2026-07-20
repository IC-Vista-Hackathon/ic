using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Pronto.Functional.Tests;

/// <summary>
/// Studio preview-tenant helpers (F2). The preview runs the same built payer PWA against the real
/// services, scoped to an isolated, seeded <c>preview-{billerId}</c> partition; these drive its
/// provision/reset/serve endpoints over the gateway.
/// </summary>
public sealed partial class ProntoApiClient
{
    public async Task<JsonNode> ProvisionPreviewAsync(string billerId, CancellationToken ct = default)
    {
        using var response = await _billerApi.PostAsync($"billers/{billerId}/preview", content: null, ct);
        var node = await ReadNodeAsync(response, ct);
        if (node["preview_biller_id"].AsStringOrNull() is { } previewBillerId)
        {
            TrackForPurge(previewBillerId);
        }

        return node;
    }

    public async Task<JsonNode> ResetPreviewAsync(string billerId, CancellationToken ct = default)
    {
        using var response = await _billerApi.PostAsync($"billers/{billerId}/preview/reset", content: null, ct);
        return await ReadNodeAsync(response, ct);
    }

    /// <summary>Fetches the preview tenant's served config the built PWA loads (the draft, pointed at the preview partition).</summary>
    public async Task<HttpResponseMessage> GetPreviewConfigAsync(string previewBillerId, CancellationToken ct = default) =>
        await _billerApi.GetAsync($"public/experiences/preview/{Uri.EscapeDataString(previewBillerId)}", ct);

    public async Task<JsonArray> GetInvoicesAsync(string billerId, string accountNumber, CancellationToken ct = default)
    {
        using var response = await _invoiceApi.GetAsync($"billers/{billerId}/invoices?account_number={accountNumber}", ct);
        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return [];
        }

        var node = await ReadNodeAsync(response, ct);
        return node["invoices"]?.AsArray() ?? [];
    }

    /// <summary>Registers an additional biller/tenant id (e.g. a preview partition) for best-effort purge on dispose.</summary>
    public void TrackForPurge(string billerId)
    {
        if (!_createdBillerIds.Contains(billerId))
        {
            _createdBillerIds.Add(billerId);
        }
    }
}
