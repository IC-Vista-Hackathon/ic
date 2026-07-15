using Pronto.Payment.Contracts.V1.Purchases;
using Pronto.BillerExperience.Contracts.V1.Billers;
using Pronto.ServiceDefaults.Errors;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Pronto.Payment.Api.Clients;

/// <summary>
/// Cross-service write: advance BillerAccount.status to purchased (and tier to the plan)
/// after a Purchase is paid. Design/entities.md documents this handoff.
/// </summary>
public interface IBillerAccountClient
{
    Task AdvanceToPurchasedAsync(
        string billerId,
        string purchaseId,
        PurchasePlan plan,
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
        string purchaseId,
        PurchasePlan plan,
        CancellationToken cancellationToken)
    {
        var request = new AdvanceBillerPurchaseRequest(
            purchaseId,
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
