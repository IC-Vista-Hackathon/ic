namespace IC.BillerExperience.Contracts.V1.Billers;

public sealed record CreateBillerRequest(
    string DisplayName,
    string Slug,
    BillerBrand Brand,
    BillerSupport Support,
    IReadOnlyList<PaymentRailReference> PaymentRails);

public sealed record BillerResponse(
    string BillerId,
    string DisplayName,
    string Slug,
    BillerBrand Brand,
    BillerSupport Support,
    IReadOnlyList<PaymentRailReference> PaymentRails,
    DateTimeOffset CreatedAt);

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
