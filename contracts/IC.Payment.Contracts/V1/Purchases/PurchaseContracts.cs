using System.Text.Json.Serialization;

namespace IC.Payment.Contracts.V1.Purchases;

public sealed record CreatePurchaseRequest(
    string BillerId,
    PurchasePlan Plan);

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
