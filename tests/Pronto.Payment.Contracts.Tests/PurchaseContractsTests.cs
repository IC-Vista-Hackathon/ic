using System.Text.Json;
using System.Text.Json.Serialization;
using Pronto.Payment.Contracts.V1.Events;
using Pronto.Payment.Contracts.V1.Purchases;
using Xunit;

namespace Pronto.Payment.Contracts.Tests;

public sealed class PurchaseContractsTests
{
    [Fact]
    public void PurchaseRequestIncludesStableIdempotencyKey()
    {
        var request = new CreatePurchaseRequest(
            "biller-1",
            PurchasePlan.Shared,
            "purchase-attempt-1");

        var json = JsonSerializer.Serialize(request, CaseInsensitive);

        Assert.Contains(
            "\"idempotency_key\":\"purchase-attempt-1\"",
            json,
            StringComparison.Ordinal);
    }

    // Wire policy: snake_case + lowercase string enums (design/contracts.md).
    private static readonly JsonSerializerOptions CaseInsensitive = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    [Fact]
    public void PurchaseResponseRoundTripsThroughJson()
    {
        var response = new PurchaseResponse(
            PurchaseId: "6a1b9d3f-0c2e-45f7-8a4d-b5e6c7f8a901",
            BillerId: "3b7c1d52-88aa-4f0e-bd21-64a9a0f3c111",
            Plan: PurchasePlan.Isolated,
            AmountCents: 199900,
            Status: PurchaseStatus.Paid);

        var roundTripped = JsonSerializer.Deserialize<PurchaseResponse>(
            JsonSerializer.Serialize(response, CaseInsensitive), CaseInsensitive);

        Assert.Equal(response, roundTripped);
    }

    [Fact]
    public void PurchaseCompletedEventRoundTripsThroughJson()
    {
        var completed = new PurchaseCompleted(
            EventId: "e-1",
            BillerId: "b-1",
            PurchaseId: "pu-1",
            Plan: PurchasePlan.Shared,
            OccurredAt: new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero));

        var roundTripped = JsonSerializer.Deserialize<PurchaseCompleted>(
            JsonSerializer.Serialize(completed, CaseInsensitive), CaseInsensitive);

        Assert.Equal(completed, roundTripped);
    }

    [Fact]
    public void CreatePurchaseRequestRoundTripsIdempotencyKey()
    {
        var request = new CreatePurchaseRequest("b-1", PurchasePlan.Shared, "purchase-op-1");

        var json = JsonSerializer.Serialize(request, CaseInsensitive);
        var roundTripped = JsonSerializer.Deserialize<CreatePurchaseRequest>(json, CaseInsensitive);

        Assert.Contains("\"idempotency_key\":\"purchase-op-1\"", json, StringComparison.Ordinal);
        Assert.Equal(request, roundTripped);
    }
}
