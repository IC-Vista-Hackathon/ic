using System.Text.Json;
using IC.BillerExperience.Contracts.V1.Billers;
using Xunit;

namespace IC.BillerExperience.Contracts.Tests;

public sealed class BillerContractsTests
{
    private static readonly JsonSerializerOptions CaseInsensitive = new(JsonSerializerDefaults.Web);

    [Fact]
    public void BillerResponseRoundTripsThroughJson()
    {
        var response = new BillerResponse(
            BillerId: "3b7c1d52-88aa-4f0e-bd21-64a9a0f3c111",
            DisplayName: "City of Plano",
            Slug: "plano",
            Brand: new BillerBrand(
                PrimaryColor: "#0044cc",
                SecondaryColor: "#ffffff",
                LogoAssetId: "asset-42",
                FontFamily: "Inter"),
            Support: new BillerSupport(
                Email: "support@plano.gov",
                Phone: "+1-972-555-0100",
                Website: new Uri("https://plano.gov")),
            PaymentRails: [new PaymentRailReference("card", "cfg-1")],
            CreatedAt: new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero));

        var roundTripped = JsonSerializer.Deserialize<BillerResponse>(
            JsonSerializer.Serialize(response, CaseInsensitive), CaseInsensitive);

        // BillerResponse holds a collection, so record equality is reference-based — compare fields.
        Assert.NotNull(roundTripped);
        Assert.Equal(response.BillerId, roundTripped.BillerId);
        Assert.Equal(response.DisplayName, roundTripped.DisplayName);
        Assert.Equal(response.Slug, roundTripped.Slug);
        Assert.Equal(response.Brand, roundTripped.Brand);
        Assert.Equal(response.Support, roundTripped.Support);
        Assert.Equal(response.PaymentRails, roundTripped.PaymentRails);
        Assert.Equal(response.CreatedAt, roundTripped.CreatedAt);
    }

    [Fact]
    public void BrandAndSupportOptionalFieldsDefaultToNull()
    {
        var brand = new BillerBrand(PrimaryColor: "#0044cc", SecondaryColor: "#ffffff");
        var support = new BillerSupport(Email: "support@plano.gov");

        Assert.Null(brand.LogoAssetId);
        Assert.Null(brand.FontFamily);
        Assert.Null(support.Phone);
        Assert.Null(support.Website);
    }

    [Fact]
    public void CreateBillerRequestDeserializesWithoutOptionalNestedProperties()
    {
        const string json =
            """
            {"displayName":"City of Plano","slug":"plano",
             "brand":{"primaryColor":"#0044cc","secondaryColor":"#ffffff"},
             "support":{"email":"support@plano.gov"},
             "paymentRails":[]}
            """;

        var request = JsonSerializer.Deserialize<CreateBillerRequest>(json, CaseInsensitive);

        Assert.NotNull(request);
        Assert.Null(request.Brand.LogoAssetId);
        Assert.Null(request.Support.Phone);
        Assert.Null(request.Support.Website);
        Assert.Empty(request.PaymentRails);
    }
}
