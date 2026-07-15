using Pronto.Payment.Contracts.V1.Purchases;
using Pronto.BillerExperience.Contracts.V1.Billers;
using Pronto.ServiceDefaults.Errors;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Pronto.Payment.Api.Clients;

/// <summary>
/// Advances the BillerAccount owned by Biller Configuration Service after purchase. Implementors
/// must treat <paramref name="idempotencyKey"/> idempotently because a successful downstream
/// transition can be retried before the local purchase is marked paid.
/// </summary>
public interface IBillerAccountClient
{
    Task AdvanceToPurchasedAsync(
        string billerId,
        PurchasePlan plan,
        string idempotencyKey,
        CancellationToken cancellationToken);
}

public sealed class HttpBillerAccountClient(HttpClient http) : IBillerAccountClient
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public async Task AdvanceToPurchasedAsync(
        string billerId,
        PurchasePlan plan,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var request = new AdvanceBillerPurchaseRequest(
            idempotencyKey,
            plan == PurchasePlan.Isolated ? BillerTier.Isolated : BillerTier.Shared);
        var response = await http.PostAsJsonAsync(
            new Uri($"billers/{billerId}/purchase", UriKind.Relative),
            request,
            Wire,
            cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw ServiceException.NotFound("biller_not_found", $"biller {billerId} not found");
        }

        throw new ServiceException(
            (int)response.StatusCode,
            "biller_purchase_failed",
            "Biller account could not be advanced to purchased.");
    }
}

public sealed class UnavailableBillerAccountClient : IBillerAccountClient
{
    public Task AdvanceToPurchasedAsync(
        string billerId,
        PurchasePlan plan,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        Task.FromException(new InvalidOperationException(
            "BillerAccount completion client is not configured; purchase remains queued for retry."));
}
