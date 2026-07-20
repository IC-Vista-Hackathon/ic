using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Pronto.Functional.Tests;

/// <summary>
/// Publish-flow helpers used by the deterministic compliance-gate functional tests (FR-8). These
/// drive the approve → publish path over the same gateway routes the Studio uses, reading the raw
/// wire contract so tests assert on the published <c>attestation</c> and problem-details findings.
/// </summary>
public sealed partial class ProntoApiClient
{
    /// <summary>
    /// Overwrites the current draft definition. Callers read the draft via <see cref="GetConfigAsync"/>,
    /// mutate the returned <c>definition</c> node, and pass it back with the revision's ETag.
    /// </summary>
    public async Task<JsonNode> UpdateConfigAsync(
        string billerId,
        JsonNode definition,
        string? expectedETag,
        CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["definition"] = definition.DeepClone(),
            ["expected_etag"] = expectedETag,
        };
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"billers/{billerId}/config")
        {
            Content = JsonContent.Create(body),
        };
        using var response = await _billerApi.SendAsync(request, ct);
        return await ReadNodeAsync(response, ct);
    }

    public async Task<JsonNode> ApproveConfigAsync(
        string billerId,
        string revision,
        string approvedBy = "functional-tests",
        CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["revision"] = revision, ["approved_by"] = approvedBy };
        using var response = await _billerApi.PostAsJsonAsync(
            $"billers/{billerId}/config/approve", body, ProntoEnvironment.Json, ct);
        return await ReadNodeAsync(response, ct);
    }

    /// <summary>
    /// Requests publication and returns the raw response so tests can assert both the 202 success
    /// body (carrying the signed attestation) and the 422 problem-details (carrying gating findings).
    /// </summary>
    public async Task<HttpResponseMessage> PublishConfigAsync(
        string billerId,
        string revision,
        CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["biller_id"] = billerId, ["revision"] = revision };
        return await _billerApi.PostAsJsonAsync(
            $"billers/{billerId}/config/publish", body, ProntoEnvironment.Json, ct);
    }
}
