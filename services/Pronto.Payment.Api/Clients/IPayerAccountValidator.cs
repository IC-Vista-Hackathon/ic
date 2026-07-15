using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pronto.ServiceDefaults.Errors;

namespace Pronto.Payment.Api.Clients;

/// <summary>
/// Validates that a <c>payer_account_id</c> supplied on a payment refers to a real payer account
/// owned by the biller. This is a seam only — the Payment Service must not own PayerAccount
/// storage (design/services.md: PayerAccount Service owns that entity). Parent integrations bind a
/// concrete validator (e.g. <see cref="HttpPayerAccountValidator"/>) via DI; the default
/// <see cref="PermissivePayerAccountValidator"/> accepts any value so guest-pay and standalone
/// local runs keep working.
/// </summary>
public interface IPayerAccountValidator
{
    /// <summary>
    /// Throws <see cref="ServiceException"/> (400/404) when <paramref name="payerAccountId"/> is
    /// present but invalid for <paramref name="billerId"/>. A null/blank id is guest pay — always ok.
    /// </summary>
    Task ValidateAsync(string billerId, string? payerAccountId, CancellationToken cancellationToken);
}

/// <summary>
/// Default seam implementation: treats a non-blank id as valid without consulting any store.
/// Keeps the Payment Service free of PayerAccount persistence while leaving a single place for a
/// parent host to swap in a real validator.
/// </summary>
public sealed class PermissivePayerAccountValidator : IPayerAccountValidator
{
    public Task ValidateAsync(string billerId, string? payerAccountId, CancellationToken cancellationToken)
    {
        if (payerAccountId is not null && string.IsNullOrWhiteSpace(payerAccountId))
        {
            throw ServiceException.BadRequest(
                "invalid_payer_account", "payer_account_id must be a non-empty id when provided.");
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Validates the id against the PayerAccount Service's point-read
/// (<c>GET /payers/{id}?biller_id=</c>). Owns no storage — it only calls the owning service.
/// Bound by a parent host via <see cref="PaymentServiceCollectionExtensions"/> when the
/// PayerAccount API endpoint is configured.
/// </summary>
public sealed class HttpPayerAccountValidator : IPayerAccountValidator
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly HttpClient http;

    public HttpPayerAccountValidator(HttpClient http)
    {
        this.http = http;
    }

    public async Task ValidateAsync(string billerId, string? payerAccountId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payerAccountId))
        {
            return;
        }

        var response = await http.GetAsync(
            new Uri($"payers/{Uri.EscapeDataString(payerAccountId)}?biller_id={Uri.EscapeDataString(billerId)}", UriKind.Relative),
            cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw ServiceException.NotFound(
                "payer_account_not_found", $"payer account {payerAccountId} not found for this biller.");
        }

        if (!response.IsSuccessStatusCode)
        {
            ErrorEnvelope? envelope = null;
            try
            {
                envelope = await response.Content
                    .ReadFromJsonAsync<ErrorEnvelope>(Wire, cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                // Non-envelope body; fall through to a generic message.
            }

            throw new ServiceException(
                (int)response.StatusCode,
                envelope?.Error.Code ?? "payer_account_service_error",
                envelope?.Error.Message ?? "Payer account service returned an unexpected error.");
        }
    }
}
