using System.Text.Json;
using System.Text.Json.Serialization;
using IC.Payment.Contracts.V1.Events;
using IC.Payment.Contracts.V1.Purchases;
using Xunit;

namespace IC.Payment.Contracts.Tests;

public sealed class PurchaseContractsTests
{
    // Wire policy: camelCase + enums as strings (design/contracts.md).
    private static readonly JsonSerializerOptions CaseInsensitive = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
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
}
