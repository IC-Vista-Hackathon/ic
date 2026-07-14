using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IC.Invoice.Contracts.V1.Invoices;
using IC.ServiceDefaults.Errors;

namespace IC.Payment.Api.Clients;

public sealed class HttpInvoiceClient : IInvoiceClient
{
    // Invoice Service wire format is snake_case (see IC.Invoice.Api Program.cs).
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly HttpClient http;

    public HttpInvoiceClient(HttpClient http)
    {
        this.http = http;
    }

    public async Task<InvoiceResponse?> GetAsync(
        string billerId, string invoiceId, CancellationToken cancellationToken)
    {
        var response = await http.GetAsync(
            new Uri($"billers/{billerId}/invoices/{invoiceId}", UriKind.Relative),
            cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content
            .ReadFromJsonAsync<InvoiceResponse>(Wire, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<InvoiceResponse> UpdateStatusAsync(
        string billerId,
        string invoiceId,
        UpdateInvoiceStatusRequest request,
        CancellationToken cancellationToken)
    {
        var response = await http.PostAsJsonAsync(
            new Uri($"billers/{billerId}/invoices/{invoiceId}/status", UriKind.Relative),
            request,
            Wire,
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var envelope = await response.Content
                .ReadFromJsonAsync<ErrorEnvelope>(Wire, cancellationToken)
                .ConfigureAwait(false);
            throw new ServiceException(
                (int)response.StatusCode,
                envelope?.Error.Code ?? "invoice_service_error",
                envelope?.Error.Message ?? "Invoice service rejected the status update.");
        }

        return (await response.Content
            .ReadFromJsonAsync<InvoiceResponse>(Wire, cancellationToken)
            .ConfigureAwait(false))!;
    }
}
