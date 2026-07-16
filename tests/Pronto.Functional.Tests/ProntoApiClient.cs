using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pronto.Functional.Tests;

/// <summary>
/// Thin HTTP helper over the deployed Biller Experience and Invoice APIs. Responses are returned
/// as <see cref="JsonNode"/> so tests read exactly the wire contract the Studio and payer PWA see,
/// without coupling to server-internal response records. Every created biller is tracked so the
/// fixture can purge it (nonprod exposes DELETE /internal/test-data).
/// </summary>
public sealed class ProntoApiClient : IDisposable
{
    private readonly HttpClient _billerApi = ProntoEnvironment.CreateBillerApiClient();
    private readonly HttpClient _invoiceApi = ProntoEnvironment.CreateInvoiceApiClient();
    private readonly List<string> _createdBillerIds = [];

    /// <summary>Account number the onboarding invoice seeder attaches demo invoices to.</summary>
    public const string SeededAccountNumber = "4421";

    public async Task<HttpResponseMessage> GetAsync(string relativeUrl, CancellationToken ct = default) =>
        await _billerApi.GetAsync(relativeUrl, ct);

    public async Task<JsonNode> CreateBillerAsync(
        string displayName,
        string billType,
        string? website,
        string postalCode = "10001",
        CancellationToken ct = default)
    {
        var slug = $"fn-{Guid.NewGuid():N}"[..16];
        var body = new Dictionary<string, object?>
        {
            ["display_name"] = displayName,
            ["slug"] = slug,
            ["bill_type"] = billType,
            ["postal_code"] = postalCode,
            ["website"] = website,
        };
        using var response = await _billerApi.PostAsJsonAsync("billers", body, ProntoEnvironment.Json, ct);
        var node = await ReadNodeAsync(response, ct);
        var billerId = node["biller"]?["biller_id"]?.GetValue<string>();
        if (billerId is not null)
        {
            _createdBillerIds.Add(billerId);
        }

        return node;
    }

    public async Task<JsonNode> GetConfigAsync(string billerId, CancellationToken ct = default)
    {
        using var response = await _billerApi.GetAsync($"billers/{billerId}/config", ct);
        return await ReadNodeAsync(response, ct);
    }

    public async Task<JsonNode> SendChatAsync(
        string billerId,
        string message,
        (string Dimension, string Answer)? billingAnswer = null,
        CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["message"] = message };
        if (billingAnswer is { } answer)
        {
            body["billing_answers"] = new[]
            {
                new Dictionary<string, object?> { ["dimension"] = answer.Dimension, ["answer"] = answer.Answer },
            };
        }

        using var response = await _billerApi.PostAsJsonAsync($"billers/{billerId}/chat", body, ProntoEnvironment.Json, ct);
        return await ReadNodeAsync(response, ct);
    }

    public async Task<JsonNode> GetActivityAsync(string billerId, CancellationToken ct = default)
    {
        using var response = await _billerApi.GetAsync($"billers/{billerId}/activity", ct);

        // The activity endpoint currently 404s for a run that has recorded zero agent events
        // (the session itself resolves fine). Treat that as an empty snapshot so callers can
        // assert on the absence of research activity rather than crashing on the read.
        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return new JsonObject { ["activity"] = new JsonArray() };
        }

        return await ReadNodeAsync(response, ct);
    }

    public async Task<JsonArray> ListSeededInvoicesAsync(string billerId, CancellationToken ct = default)
    {
        using var response = await _invoiceApi.GetAsync(
            $"billers/{billerId}/invoices?account_number={SeededAccountNumber}", ct);
        var node = await ReadNodeAsync(response, ct);
        return node["invoices"]?.AsArray() ?? [];
    }

    public async Task<bool> TryPurgeAsync(string billerId, CancellationToken ct = default)
    {
        // Each service owns its own store (no cross-service cascade — see CLAUDE.md), so purge the
        // biller-experience records (configs/runs/deployments/biller) and the seeded demo invoices
        // independently. The suite never creates PayerAccount records, so there is nothing to purge
        // there. Both endpoints are nonprod-only (Maintenance:PurgeEnabled) and return 204.
        using var billerResponse = await _billerApi.DeleteAsync($"internal/test-data?biller_id={billerId}", ct);
        using var invoiceResponse = await _invoiceApi.DeleteAsync($"internal/test-data?biller_id={billerId}", ct);
        return billerResponse.StatusCode is HttpStatusCode.NoContent
            && invoiceResponse.StatusCode is HttpStatusCode.NoContent;
    }

    private static async Task<JsonNode> ReadNodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var content = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new ProntoApiException(response.RequestMessage?.RequestUri, response.StatusCode, content);
        }

        return JsonNode.Parse(content)
            ?? throw new ProntoApiException(response.RequestMessage?.RequestUri, response.StatusCode, "empty body");
    }

    public void Dispose()
    {
        foreach (var billerId in _createdBillerIds)
        {
            try
            {
                TryPurgeAsync(billerId).GetAwaiter().GetResult();
            }
            catch
            {
                // Best-effort cleanup: purge is nonprod-only and must never fail a test run.
            }
        }

        _billerApi.Dispose();
        _invoiceApi.Dispose();
    }
}

public sealed class ProntoApiException(Uri? requestUri, HttpStatusCode statusCode, string body)
    : Exception($"Pronto API call to {requestUri} failed with {(int)statusCode} {statusCode}: {body}")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
