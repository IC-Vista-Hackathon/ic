using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pronto.Invoice.Contracts.V1.Invoices;
using Pronto.Payment.Contracts.V1.Payments;
using Pronto.PayerAccount.Contracts.V1.Payers;

namespace Pronto.BillerExperience.Api.Infrastructure.Mcp.ServiceClients;

/// <summary>
/// Typed, biller-scoped clients that sit behind every MCP service tool. The orchestrator never
/// reaches these services directly; it goes through the MCP router, which validates a capability
/// and then calls the matching client. Identity (biller_id/payer_id) always comes from the
/// validated capability, never from a client argument.
/// </summary>
public interface IInvoiceServiceClient
{
    ValueTask<InvoiceListResponse> ListAsync(string billerId, string accountNumber, bool includeClosed, CancellationToken cancellationToken);
    ValueTask<InvoiceResponse?> GetAsync(string billerId, string invoiceId, CancellationToken cancellationToken);
    ValueTask<SeedInvoicesResponse> SeedAsync(string billerId, SeedInvoicesRequest request, CancellationToken cancellationToken);
}

public interface IPaymentServiceClient
{
    ValueTask<PaymentQuoteResponse> GetQuoteAsync(string billerId, string invoiceId, string method, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<PaymentResponse>> ListAsync(string billerId, string payerAccountId, CancellationToken cancellationToken);

    /// <summary>
    /// Execute a payment via <c>POST /payments</c>, passing <paramref name="idempotencyKey"/> in the
    /// <c>Idempotency-Key</c> header so a retried submission of the same confirmed intent resolves to
    /// the original payment instead of charging twice. The Payment Service owns state transitions.
    /// </summary>
    ValueTask<PaymentResponse> CreateAsync(CreatePaymentRequest request, string idempotencyKey, CancellationToken cancellationToken);
}

public interface IPayerAccountServiceClient
{
    ValueTask<PayerResponse?> FindByAccountAsync(string billerId, string accountNumber, CancellationToken cancellationToken);
    ValueTask<PayerResponse?> GetAsync(string billerId, string payerId, CancellationToken cancellationToken);
    ValueTask<PayerPreferences> UpdatePreferencesAsync(string billerId, string payerId, UpdatePayerPreferencesRequest request, CancellationToken cancellationToken);
    ValueTask<PayerResponse> RegisterAsync(RegisterPayerRequest request, CancellationToken cancellationToken);
}

internal static class ServiceClientJson
{
    public static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) },
    };

    public static async ValueTask<T> ReadRequiredAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(WireOptions, cancellationToken)
            ?? throw new InvalidOperationException($"The downstream service returned an empty {typeof(T).Name} body.");
    }
}

public sealed class HttpInvoiceServiceClient(HttpClient http) : IInvoiceServiceClient
{
    public async ValueTask<InvoiceListResponse> ListAsync(string billerId, string accountNumber, bool includeClosed, CancellationToken cancellationToken)
    {
        var path = $"billers/{Uri.EscapeDataString(billerId)}/invoices" +
            $"?account_number={Uri.EscapeDataString(accountNumber)}&include_closed={(includeClosed ? "true" : "false")}";
        using var response = await http.GetAsync(path, cancellationToken).ConfigureAwait(false);
        return await ServiceClientJson.ReadRequiredAsync<InvoiceListResponse>(response, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<InvoiceResponse?> GetAsync(string billerId, string invoiceId, CancellationToken cancellationToken)
    {
        var path = $"billers/{Uri.EscapeDataString(billerId)}/invoices/{Uri.EscapeDataString(invoiceId)}";
        using var response = await http.GetAsync(path, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        return await ServiceClientJson.ReadRequiredAsync<InvoiceResponse>(response, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<SeedInvoicesResponse> SeedAsync(string billerId, SeedInvoicesRequest request, CancellationToken cancellationToken)
    {
        var path = $"billers/{Uri.EscapeDataString(billerId)}/invoices/seed";
        using var response = await http.PostAsJsonAsync(path, request, ServiceClientJson.WireOptions, cancellationToken).ConfigureAwait(false);
        return await ServiceClientJson.ReadRequiredAsync<SeedInvoicesResponse>(response, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class HttpPaymentServiceClient(HttpClient http) : IPaymentServiceClient
{
    public async ValueTask<PaymentQuoteResponse> GetQuoteAsync(string billerId, string invoiceId, string method, CancellationToken cancellationToken)
    {
        var path = $"payments/quote?biller_id={Uri.EscapeDataString(billerId)}" +
            $"&invoice_id={Uri.EscapeDataString(invoiceId)}&method={Uri.EscapeDataString(method)}";
        using var response = await http.GetAsync(path, cancellationToken).ConfigureAwait(false);
        return await ServiceClientJson.ReadRequiredAsync<PaymentQuoteResponse>(response, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<PaymentResponse>> ListAsync(string billerId, string payerAccountId, CancellationToken cancellationToken)
    {
        var path = $"payments?biller_id={Uri.EscapeDataString(billerId)}" +
            $"&payer_account_id={Uri.EscapeDataString(payerAccountId)}";
        using var response = await http.GetAsync(path, cancellationToken).ConfigureAwait(false);
        return await ServiceClientJson.ReadRequiredAsync<IReadOnlyList<PaymentResponse>>(response, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<PaymentResponse> CreateAsync(CreatePaymentRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "payments")
        {
            Content = JsonContent.Create(request, options: ServiceClientJson.WireOptions),
        };
        message.Headers.Add("Idempotency-Key", idempotencyKey);
        using var response = await http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        return await ServiceClientJson.ReadRequiredAsync<PaymentResponse>(response, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class HttpPayerAccountServiceClient(HttpClient http) : IPayerAccountServiceClient
{
    public async ValueTask<PayerResponse?> FindByAccountAsync(string billerId, string accountNumber, CancellationToken cancellationToken)
    {
        var path = $"payers?biller_id={Uri.EscapeDataString(billerId)}&account_number={Uri.EscapeDataString(accountNumber)}";
        using var response = await http.GetAsync(path, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        return await ServiceClientJson.ReadRequiredAsync<PayerResponse>(response, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<PayerResponse?> GetAsync(string billerId, string payerId, CancellationToken cancellationToken)
    {
        var path = $"payers/{Uri.EscapeDataString(payerId)}?biller_id={Uri.EscapeDataString(billerId)}";
        using var response = await http.GetAsync(path, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        return await ServiceClientJson.ReadRequiredAsync<PayerResponse>(response, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<PayerPreferences> UpdatePreferencesAsync(string billerId, string payerId, UpdatePayerPreferencesRequest request, CancellationToken cancellationToken)
    {
        var path = $"payers/{Uri.EscapeDataString(payerId)}/preferences?biller_id={Uri.EscapeDataString(billerId)}";
        using var response = await http.PatchAsJsonAsync(path, request, ServiceClientJson.WireOptions, cancellationToken).ConfigureAwait(false);
        return await ServiceClientJson.ReadRequiredAsync<PayerPreferences>(response, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<PayerResponse> RegisterAsync(RegisterPayerRequest request, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync("payers", request, ServiceClientJson.WireOptions, cancellationToken).ConfigureAwait(false);
        return await ServiceClientJson.ReadRequiredAsync<PayerResponse>(response, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Registered when a downstream base URL is not configured; fails fast with a clear reason.</summary>
public sealed class UnavailableServiceClient(string serviceName)
    : IInvoiceServiceClient, IPaymentServiceClient, IPayerAccountServiceClient
{
    private InvalidOperationException NotConfigured() =>
        new($"The {serviceName} base URL is not configured; the corresponding MCP tools are unavailable.");

    ValueTask<InvoiceListResponse> IInvoiceServiceClient.ListAsync(string billerId, string accountNumber, bool includeClosed, CancellationToken cancellationToken) => throw NotConfigured();
    ValueTask<InvoiceResponse?> IInvoiceServiceClient.GetAsync(string billerId, string invoiceId, CancellationToken cancellationToken) => throw NotConfigured();
    ValueTask<SeedInvoicesResponse> IInvoiceServiceClient.SeedAsync(string billerId, SeedInvoicesRequest request, CancellationToken cancellationToken) => throw NotConfigured();
    ValueTask<PaymentQuoteResponse> IPaymentServiceClient.GetQuoteAsync(string billerId, string invoiceId, string method, CancellationToken cancellationToken) => throw NotConfigured();
    ValueTask<IReadOnlyList<PaymentResponse>> IPaymentServiceClient.ListAsync(string billerId, string payerAccountId, CancellationToken cancellationToken) => throw NotConfigured();
    ValueTask<PaymentResponse> IPaymentServiceClient.CreateAsync(CreatePaymentRequest request, string idempotencyKey, CancellationToken cancellationToken) => throw NotConfigured();
    ValueTask<PayerResponse?> IPayerAccountServiceClient.FindByAccountAsync(string billerId, string accountNumber, CancellationToken cancellationToken) => throw NotConfigured();
    ValueTask<PayerResponse?> IPayerAccountServiceClient.GetAsync(string billerId, string payerId, CancellationToken cancellationToken) => throw NotConfigured();
    ValueTask<PayerPreferences> IPayerAccountServiceClient.UpdatePreferencesAsync(string billerId, string payerId, UpdatePayerPreferencesRequest request, CancellationToken cancellationToken) => throw NotConfigured();
    ValueTask<PayerResponse> IPayerAccountServiceClient.RegisterAsync(RegisterPayerRequest request, CancellationToken cancellationToken) => throw NotConfigured();
}
