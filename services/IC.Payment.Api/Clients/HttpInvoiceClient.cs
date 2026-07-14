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

        if (!response.IsSuccessStatusCode)
        {
            throw await ToServiceExceptionAsync(response, cancellationToken).ConfigureAwait(false);
        }

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
            throw await ToServiceExceptionAsync(response, cancellationToken).ConfigureAwait(false);
        }

        return (await response.Content
            .ReadFromJsonAsync<InvoiceResponse>(Wire, cancellationToken)
            .ConfigureAwait(false))!;
    }

    private static async Task<ServiceException> ToServiceExceptionAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        ErrorEnvelope? envelope = null;
        try
        {
            envelope = await response.Content
                .ReadFromJsonAsync<ErrorEnvelope>(Wire, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            // Non-envelope body (proxy error page etc.) — fall through to the generic message.
        }

        return new ServiceException(
            (int)response.StatusCode,
            envelope?.Error.Code ?? "invoice_service_error",
            envelope?.Error.Message ?? "Invoice service returned an unexpected error.");
    }
}
