using System.Text.Json;
using IC.BillerExperience.Contracts.V1.Experiences;
using Xunit;

namespace IC.BillerExperience.Contracts.Tests;

public sealed class ExperienceContractsTests
{
    private static readonly JsonSerializerOptions CaseInsensitive = new(JsonSerializerDefaults.Web);

    private static BillerExperienceDefinition SampleDefinition() => new(
        SchemaVersion: "1.0",
        BillerId: "3b7c1d52-88aa-4f0e-bd21-64a9a0f3c111",
        Brand: new ExperienceBrand(
            DisplayName: "City of Plano",
            PrimaryColor: "#0044cc",
            SecondaryColor: "#ffffff",
            LogoAssetId: "asset-42",
            FontFamily: "Inter"),
        Content: new ExperienceContent(
            Heading: "Pay your utility bill",
            Introduction: "Fast, secure payments for Plano residents.",
            SupportText: "Questions? Contact support@plano.gov.",
            PrivacyPolicyUrl: new Uri("https://plano.gov/privacy"),
            TermsOfServiceUrl: new Uri("https://plano.gov/terms")),
        Pwa: new PwaConfiguration(
            Name: "Plano Utility Payments",
            ShortName: "Plano Pay",
            ThemeColor: "#0044cc",
            BackgroundColor: "#ffffff",
            IconAssetId: "asset-43"),
        EnabledPaymentCapabilities: ["card", "ach"]);

    [Fact]
    public void ExperienceRevisionResponseRoundTripsThroughJson()
    {
        var response = new ExperienceRevisionResponse(
            BillerId: "3b7c1d52-88aa-4f0e-bd21-64a9a0f3c111",
            Revision: "rev-7",
            Definition: SampleDefinition(),
            State: ExperienceRevisionState.Approved,
            CreatedAt: new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero),
            ApprovedAt: new DateTimeOffset(2026, 7, 14, 13, 30, 0, TimeSpan.Zero));

        var roundTripped = JsonSerializer.Deserialize<ExperienceRevisionResponse>(
            JsonSerializer.Serialize(response, CaseInsensitive), CaseInsensitive);

        // The definition holds a collection, so record equality is reference-based — compare fields.
        Assert.NotNull(roundTripped);
        Assert.Equal(response.Revision, roundTripped.Revision);
        Assert.Equal(response.State, roundTripped.State);
        Assert.Equal(response.ApprovedAt, roundTripped.ApprovedAt);
        Assert.Equal(response.Definition.SchemaVersion, roundTripped.Definition.SchemaVersion);
        Assert.Equal(response.Definition.Brand, roundTripped.Definition.Brand);
        Assert.Equal(response.Definition.Content, roundTripped.Definition.Content);
        Assert.Equal(response.Definition.Pwa, roundTripped.Definition.Pwa);
        Assert.Equal(
            response.Definition.EnabledPaymentCapabilities,
            roundTripped.Definition.EnabledPaymentCapabilities);
    }

    [Fact]
    public void DraftRevisionSerializesNullApprovedAt()
    {
        var response = new ExperienceRevisionResponse(
            BillerId: "3b7c1d52-88aa-4f0e-bd21-64a9a0f3c111",
            Revision: "rev-1",
            Definition: SampleDefinition(),
            State: ExperienceRevisionState.Draft,
            CreatedAt: new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero),
            ApprovedAt: null);

        var roundTripped = JsonSerializer.Deserialize<ExperienceRevisionResponse>(
            JsonSerializer.Serialize(response, CaseInsensitive), CaseInsensitive);

        Assert.NotNull(roundTripped);
        Assert.Equal(ExperienceRevisionState.Draft, roundTripped.State);
        Assert.Null(roundTripped.ApprovedAt);
    }

    [Fact]
    public void ApproveExperienceRequestRoundTripsThroughJson()
    {
        var request = new ApproveExperienceRequest(Revision: "rev-7", ApprovedBy: "ddominguez");

        var roundTripped = JsonSerializer.Deserialize<ApproveExperienceRequest>(
            JsonSerializer.Serialize(request, CaseInsensitive), CaseInsensitive);

        Assert.Equal(request, roundTripped);
    }
}
