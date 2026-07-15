using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pronto.PayerAccount.Api.Accounts;

/// <summary>
/// Establishes account ownership against the Invoice Service: an account number is linkable
/// when the biller has at least one invoice (open or closed) seeded for it
/// (<c>GET /billers/{billerId}/invoices?account_number=&amp;include_closed=true</c>). Invoices are
/// seeded per real account at onboarding, so their presence is the authoritative signal that the
/// account exists under the biller. Wire format is snake_case (see Pronto.Invoice.Api).
/// </summary>
public sealed partial class HttpAccountOwnershipVerifier : IAccountOwnershipVerifier
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly HttpClient http;
    private readonly ILogger<HttpAccountOwnershipVerifier> logger;

    public HttpAccountOwnershipVerifier(HttpClient http, ILogger<HttpAccountOwnershipVerifier> logger)
    {
        this.http = http;
        this.logger = logger;
    }

    public async Task<bool> IsOwnedAsync(
        string billerId, string accountNumber, CancellationToken cancellationToken = default)
    {
        var relative =
            $"billers/{Uri.EscapeDataString(billerId)}/invoices" +
            $"?account_number={Uri.EscapeDataString(accountNumber)}&include_closed=true";

        var response = await http.GetAsync(new Uri(relative, UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();

        var invoices = await response.Content
            .ReadFromJsonAsync<InvoiceListEnvelope>(Wire, cancellationToken)
            .ConfigureAwait(false);

        var owned = invoices is { Invoices.Count: > 0 };
        LogVerified(logger, billerId, accountNumber, owned);
        return owned;
    }

    // Minimal shape of Pronto.Invoice.Api's InvoiceListResponse; only the count matters here, so
    // we avoid taking a project reference on the Invoice contracts just to verify ownership.
    private sealed record InvoiceListEnvelope(
        [property: JsonPropertyName("invoices")] IReadOnlyList<JsonElement> Invoices);

    [LoggerMessage(5110, LogLevel.Information,
        "Account ownership check for biller {BillerId}, account {AccountNumber}: owned={Owned}")]
    private static partial void LogVerified(ILogger logger, string billerId, string accountNumber, bool owned);
}
