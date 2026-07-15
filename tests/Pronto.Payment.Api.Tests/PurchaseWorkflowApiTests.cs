using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pronto.Payment.Api.Clients;
using Pronto.Payment.Contracts.V1.Purchases;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Pronto.Payment.Api.Tests;

public sealed class PurchaseWorkflowApiTests
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory, FakeBillerAccountClient biller) =>
        factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.Replace(ServiceDescriptor.Singleton<IBillerAccountClient>(biller))))
            .CreateClient();

    [Fact]
    public async Task DefaultHostFailsClosedWhenBillerAccountClientIsUnavailable()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "purchases",
            new CreatePurchaseRequest(Guid.NewGuid().ToString(), PurchasePlan.Shared, "default-host-op"),
            Wire);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var purchase = await response.Content.ReadFromJsonAsync<PurchaseResponse>(Wire);
        Assert.Equal(PurchaseStatus.Pending, purchase!.Status);
    }

    [Fact]
    public async Task FailedBillerTransitionLeavesPurchasePending()
    {
        using var factory = new WebApplicationFactory<Program>();
        var biller = new FakeBillerAccountClient { FailuresBeforeSuccess = int.MaxValue };
        var client = CreateClient(factory, biller);
        var billerId = Guid.NewGuid().ToString();

        var response = await client.PostAsJsonAsync(
            "purchases", new CreatePurchaseRequest(billerId, PurchasePlan.Shared), Wire);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var purchase = await response.Content.ReadFromJsonAsync<PurchaseResponse>(Wire);
        Assert.Equal(PurchaseStatus.Pending, purchase!.Status);

        var fetched = await client.GetFromJsonAsync<PurchaseResponse>(
            $"purchases/{purchase.PurchaseId}?biller_id={billerId}", Wire);
        Assert.Equal(PurchaseStatus.Pending, fetched!.Status);
        Assert.Empty(biller.Advances);
    }

    [Fact]
    public async Task IdempotentRetryCompletesPreviouslyPendingPurchase()
    {
        using var factory = new WebApplicationFactory<Program>();
        var biller = new FakeBillerAccountClient { FailuresBeforeSuccess = 1 };
        var client = CreateClient(factory, biller);
        var billerId = Guid.NewGuid().ToString();
        var request = new CreatePurchaseRequest(billerId, PurchasePlan.Isolated, IdempotencyKey: "op-123");

        var first = await client.PostAsJsonAsync("purchases", request, Wire);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var pending = await first.Content.ReadFromJsonAsync<PurchaseResponse>(Wire);
        Assert.Equal(PurchaseStatus.Pending, pending!.Status);

        var second = await client.PostAsJsonAsync("purchases", request, Wire);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var paid = await second.Content.ReadFromJsonAsync<PurchaseResponse>(Wire);
        Assert.Equal(pending.PurchaseId, paid!.PurchaseId);
        Assert.Equal(PurchaseStatus.Paid, paid.Status);
    }

    [Fact]
    public async Task IdempotentRetryReturnsSamePurchaseWithoutDuplicateAdvance()
    {
        using var factory = new WebApplicationFactory<Program>();
        var biller = new FakeBillerAccountClient();
        var client = CreateClient(factory, biller);
        var billerId = Guid.NewGuid().ToString();
        var request = new CreatePurchaseRequest(billerId, PurchasePlan.Shared, IdempotencyKey: "op-abc");

        var first = await client.PostAsJsonAsync("purchases", request, Wire);
        var second = await client.PostAsJsonAsync("purchases", request, Wire);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var firstPurchase = await first.Content.ReadFromJsonAsync<PurchaseResponse>(Wire);
        var secondPurchase = await second.Content.ReadFromJsonAsync<PurchaseResponse>(Wire);
        Assert.Equal(firstPurchase!.PurchaseId, secondPurchase!.PurchaseId);
        Assert.Single(biller.Advances);
    }

    [Fact]
    public async Task ConflictingIdempotencyKeyIsRejected()
    {
        using var factory = new WebApplicationFactory<Program>();
        var biller = new FakeBillerAccountClient();
        var client = CreateClient(factory, biller);
        var billerId = Guid.NewGuid().ToString();

        var first = await client.PostAsJsonAsync(
            "purchases", new CreatePurchaseRequest(billerId, PurchasePlan.Shared, "key-1"), Wire);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var conflicting = await client.PostAsJsonAsync(
            "purchases", new CreatePurchaseRequest(billerId, PurchasePlan.Shared, "key-2"), Wire);
        Assert.Equal(HttpStatusCode.Conflict, conflicting.StatusCode);
    }
}
