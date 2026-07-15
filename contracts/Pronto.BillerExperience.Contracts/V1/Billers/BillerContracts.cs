using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pronto.BillerExperience.Contracts.V1.Billers;

public sealed record CreateBillerRequest(
    string DisplayName,
    string Slug,
    string BillType,
    string PostalCode,
    Uri? Website = null,
    BillerBrand? Brand = null,
    BillerSupport? Support = null,
    IReadOnlyList<PaymentRailReference>? PaymentRails = null);

public sealed record BillerResponse(
    string BillerId,
    string DisplayName,
    string Slug,
    string BillType,
    string PostalCode,
    Uri? Website,
    BillerBrand? Brand,
    BillerSupport? Support,
    IReadOnlyList<PaymentRailReference> PaymentRails,
    BillerStatus Status,
    DateTimeOffset CreatedAt,
    BillerTier Tier = BillerTier.Shared);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AdvanceBillerPurchaseRequest(
    string PurchaseId,
    BillerTier Tier);

public enum BillerStatus
{
    Prospect,
    Demo,
    Purchased,
    Live
}

[JsonConverter(typeof(BillerTierJsonConverter))]
public enum BillerTier
{
    Shared,
    Isolated
}

public sealed class BillerTierJsonConverter : JsonStringEnumConverter<BillerTier>
{
    public BillerTierJsonConverter()
        : base(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false)
    {
    }
}

public sealed record BillerBrand(
    string PrimaryColor,
    string SecondaryColor,
    string? LogoAssetId = null,
    string? FontFamily = null);

public sealed record BillerSupport(
    string Email,
    string? Phone = null,
    Uri? Website = null);

public sealed record PaymentRailReference(
    string Capability,
    string ExistingConfigurationId);
