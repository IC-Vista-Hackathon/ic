using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Pronto.Functional.Tests;

/// <summary>
/// Payer-chat helpers. One POST is one turn through the payer agent pipeline, which (post #92)
/// resolves the bill and payment quotes through the MCP router against the real Invoice/Payment
/// services and returns a grounded reply + artifacts. Identity for money movement lives in the
/// downstream capability, never in these request args.
/// </summary>
public sealed partial class ProntoApiClient
{
    public async Task<JsonNode> SendPayerChatAsync(
        string billerId,
        string invoiceId,
        string? accountNumber = null,
        string? payerMessage = null,
        CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["invoice_id"] = invoiceId,
            ["account_number"] = accountNumber,
        };
        if (payerMessage is not null)
        {
            body["messages"] = new JsonArray(
                new JsonObject { ["role"] = "user", ["content"] = payerMessage });
        }

        using var response = await _billerApi.PostAsJsonAsync(
            $"billers/{billerId}/payer-chat", body, ProntoEnvironment.Json, ct);
        return await ReadNodeAsync(response, ct);
    }
}
