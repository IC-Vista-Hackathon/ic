using System.Text.Json;
using IC.BillerExperience.Contracts.V1.Experiences;
using IC.BillerExperience.Worker;
using IC.BillerExperience.Worker.Artifacts;
using IC.BillerExperience.Worker.Persistence;
using Microsoft.Extensions.Options;
using Xunit;

namespace IC.BillerExperience.Worker.Tests;

public sealed class PublicationArtifactPlanFactoryTests
{
    [Fact]
    public void CreatesVersionedAndActiveArtifactsForSharedRenderer()
    {
        var factory = new PublicationArtifactPlanFactory(Options.Create(new PublicationOptions
        {
            PublicBaseUrl = "https://pay.example.test"
        }));
        var requestedAt = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
        var definition = Definition();

        var plan = factory.Create(
            new PublicationDeployment("deployment-3", "biller-1", 3, "applying", requestedAt),
            new PublicationBiller("biller-1", "City of Vista", "city-of-vista"),
            new PublicationExperience("config-3", "biller-1", 3, definition));

        Assert.Equal("billers/city-of-vista/revisions/config-3", plan.RevisionPrefix);
        Assert.Equal("billers/city-of-vista/active.json", plan.ActiveBlobName);
        Assert.Equal(new Uri("https://pay.example.test/pay/city-of-vista/"), plan.PublishedUrl);
        Assert.Equal(requestedAt, plan.Artifact.PublishedAt);
        Assert.Contains("\"revision\": \"config-3\"", plan.ConfigJson, StringComparison.Ordinal);
        Assert.Contains("\"start_url\": \"/pay/city-of-vista/\"", plan.ManifestJson, StringComparison.Ordinal);
    }

    [Fact]
    public void ArtifactRoundTripsWithSnakeCaseContract()
    {
        var factory = new PublicationArtifactPlanFactory(Options.Create(new PublicationOptions()));
        var plan = factory.Create(
            new PublicationDeployment("deployment-1", "biller-1", 1, "applying", DateTimeOffset.UtcNow),
            new PublicationBiller("biller-1", "City", "city"),
            new PublicationExperience("config-1", "biller-1", 1, Definition()));

        using var json = JsonDocument.Parse(plan.ConfigJson);
        Assert.Equal("biller-1", json.RootElement.GetProperty("biller_id").GetString());
        Assert.Equal("#085368", json.RootElement.GetProperty("definition").GetProperty("brand").GetProperty("primary_color").GetString());
    }

    private static BillerExperienceDefinition Definition() => new(
        "1.0",
        "biller-1",
        new ExperienceBrand("City of Vista", "#085368", "#18B4E9", null, "Inter"),
        new ExperienceContent("Pay your bill", "Welcome", "Call us", new Uri("https://example.test/privacy"), new Uri("https://example.test/terms")),
        new PwaConfiguration("City Payments", "City Pay", "#085368", "#FFFFFF", null),
        ["card", "ach"]);
}
