using System.Text.Json.Serialization;

namespace Pronto.Payment.Contracts.V1.Purchases;

/// <summary>
/// Money-moving request: unknown members are rejected (<see cref="JsonUnmappedMemberHandling.Disallow"/>)
/// so a caller can't smuggle unexpected fields past validation into a platform purchase.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreatePurchaseRequest(
    string BillerId,
    PurchasePlan Plan,
    string IdempotencyKey);

public sealed record PurchaseResponse(
    string PurchaseId,
    string BillerId,
    PurchasePlan Plan,
    int AmountCents,
    PurchaseStatus Status);

/// <summary>Wire tokens pinned at the type level so serialization is host-independent.</summary>
[JsonConverter(typeof(PurchasePlanJsonConverter))]
public enum PurchasePlan
{
    [JsonStringEnumMemberName("shared")]
    Shared,

    [JsonStringEnumMemberName("isolated")]
    Isolated,
}

/// <summary>Wire tokens pinned at the type level so serialization is host-independent.</summary>
[JsonConverter(typeof(PurchaseStatusJsonConverter))]
public enum PurchaseStatus
{
    [JsonStringEnumMemberName("pending")]
    Pending,

    [JsonStringEnumMemberName("paid")]
    Paid,
}

/// <summary>String-only converter for <see cref="PurchasePlan"/> (rejects integer tokens).</summary>
public sealed class PurchasePlanJsonConverter : JsonStringEnumConverter<PurchasePlan>
{
    public PurchasePlanJsonConverter()
        : base(namingPolicy: null, allowIntegerValues: false)
    {
    }
}

/// <summary>String-only converter for <see cref="PurchaseStatus"/> (rejects integer tokens).</summary>
public sealed class PurchaseStatusJsonConverter : JsonStringEnumConverter<PurchaseStatus>
{
    public PurchaseStatusJsonConverter()
        : base(namingPolicy: null, allowIntegerValues: false)
    {
    }
}
