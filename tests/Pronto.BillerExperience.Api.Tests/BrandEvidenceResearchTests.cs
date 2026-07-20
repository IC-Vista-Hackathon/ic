using Pronto.BillerExperience.Api.Domain;
using Pronto.BillerExperience.Api.Infrastructure.Research;
using Pronto.BillerExperience.Contracts.V1.Billers;
using Pronto.BillerExperience.Contracts.V1.Experiences;
using Pronto.BillerExperience.Contracts.V1.Research;
using Xunit;

namespace Pronto.BillerExperience.Api.Tests;

public sealed class BrandEvidenceResearchTests
{
    private static readonly Uri PageUri = new("https://www.happypants.example/");

    private const string Fixture = """
        <!doctype html>
        <html>
        <head>
          <title>Happy Pants NYC | Comfortable clothing</title>
          <meta name="theme-color" content="#ff5a1f">
          <meta name="description" content="Comfy pants, delivered.">
          <meta property="og:site_name" content="Happy Pants NYC">
          <meta property="og:description" content="The comfiest pants in the city.">
          <meta property="og:image" content="https://cdn.other-domain.example/social.png">
          <link rel="icon" href="/favicon-32.png">
          <link rel="apple-touch-icon" href="/apple-touch-icon.png">
          <style>
            :root { --brand: #ff5a1f; }
            body { font-family: "Poppins", Helvetica, sans-serif; color: #222222; }
            .cta { background: #1e88e5; color: #ffffff; }
            .cta:hover { background: #1e88e5; }
          </style>
        </head>
        <body>
          <header><img class="site-logo" src="/img/logo.svg" alt="Happy Pants logo"></header>
          <h1>Welcome to Happy Pants</h1>
        </body>
        </html>
        """;

    [Fact]
    public void ExtractPullsLogoColorsFontAndCopyFromFirstPartyMarkup()
    {
        var facts = BrandEvidenceExtractor.Extract(Fixture, PageUri);

        // apple-touch-icon wins the logo race and resolves same-origin absolute.
        Assert.Equal("https://www.happypants.example/apple-touch-icon.png", Value(facts, BrandEvidenceFacts.LogoUrl));
        // theme-color leads the palette; the most frequent non-neutral CSS color is secondary.
        Assert.Equal("#ff5a1f", Value(facts, BrandEvidenceFacts.PrimaryColor));
        Assert.Equal("#1e88e5", Value(facts, BrandEvidenceFacts.SecondaryColor));
        // First non-generic font family only.
        Assert.Equal("Poppins", Value(facts, BrandEvidenceFacts.FontFamily));
        Assert.Equal("Happy Pants NYC", Value(facts, BrandEvidenceFacts.DisplayName));
        Assert.Equal("The comfiest pants in the city.", Value(facts, BrandEvidenceFacts.Tagline));

        // Every fact cites the page it came from.
        Assert.All(facts, fact => Assert.Equal(PageUri, fact.SourceUrl));
    }

    [Fact]
    public void ExtractRejectsOffOriginAndNeutralOnlySignals()
    {
        // og:image is on a different host, and there is no same-origin icon/logo, so no logo fact.
        const string html = """
            <html><head>
              <meta property="og:image" content="https://cdn.elsewhere.example/logo.png">
              <style> body { color: #ffffff; background: #111111; } </style>
            </head><body></body></html>
            """;

        var facts = BrandEvidenceExtractor.Extract(html, PageUri);

        Assert.Null(Value(facts, BrandEvidenceFacts.LogoUrl));
        // Only neutral colors present, so no brand color is asserted.
        Assert.Null(Value(facts, BrandEvidenceFacts.PrimaryColor));
    }

    [Fact]
    public void ExtractExpandsShorthandHexAndFallsBackToRelIcon()
    {
        const string html = """
            <html><head>
              <link rel="shortcut icon" href="/brand/icon.ico">
              <style> a { color: #0af; } </style>
            </head><body></body></html>
            """;

        var facts = BrandEvidenceExtractor.Extract(html, PageUri);

        Assert.Equal("https://www.happypants.example/brand/icon.ico", Value(facts, BrandEvidenceFacts.LogoUrl));
        Assert.Equal("#00aaff", Value(facts, BrandEvidenceFacts.PrimaryColor));
    }

    [Fact]
    public void ApplyMapsEvidenceOntoUnbrandedDraft()
    {
        var definition = UnbrandedDefinition();
        var research = ResearchWith(
            (BrandEvidenceFacts.PrimaryColor, "#ff5a1f"),
            (BrandEvidenceFacts.SecondaryColor, "#1e88e5"),
            (BrandEvidenceFacts.LogoUrl, "https://vista.example/logo.svg"),
            (BrandEvidenceFacts.FontFamily, "Poppins"),
            (BrandEvidenceFacts.Tagline, "The comfiest pants in the city."));

        var applied = ResearchBrandApplicator.Apply(definition, Biller(), research);

        Assert.Equal("#ff5a1f", applied.Brand.PrimaryColor);
        Assert.Equal("#1e88e5", applied.Brand.SecondaryColor);
        Assert.Equal("https://vista.example/logo.svg", applied.Brand.LogoAssetId);
        Assert.Equal("Poppins", applied.Brand.FontFamily);
        Assert.Equal("#ff5a1f", applied.Pwa.ThemeColor);
        Assert.NotNull(applied.Brief);
        Assert.Contains(applied.Brief!.Assets, asset => asset.Kind == "logo");
    }

    [Fact]
    public void ApplyPreservesExplicitBrandTokensAndRejectsOffOriginLogo()
    {
        var definition = UnbrandedDefinition() with
        {
            Brand = new ExperienceBrand("City of Vista", "#174A5B", string.Empty, null, null)
        };
        var research = ResearchWith(
            (BrandEvidenceFacts.PrimaryColor, "#ff5a1f"),
            (BrandEvidenceFacts.SecondaryColor, "#1e88e5"),
            (BrandEvidenceFacts.LogoUrl, "https://evil.example/logo.svg"));

        var applied = ResearchBrandApplicator.Apply(definition, Biller(), research);

        Assert.Equal("#174A5B", applied.Brand.PrimaryColor); // explicit value untouched
        Assert.Equal("#1e88e5", applied.Brand.SecondaryColor); // filled because it was blank
        Assert.Null(applied.Brand.LogoAssetId); // off-origin logo rejected
    }

    [Fact]
    public void ApplyDerivesSecondaryFromPrimaryWhenSiteHasOneColor()
    {
        var definition = UnbrandedDefinition();
        var research = ResearchWith(
            (BrandEvidenceFacts.PrimaryColor, "#ff5a1f"),
            (BrandEvidenceFacts.LogoUrl, "https://vista.example/logo.svg"));

        var applied = ResearchBrandApplicator.Apply(definition, Biller(), research);

        Assert.Equal("#ff5a1f", applied.Brand.PrimaryColor);
        // A single researched color yields a derived, on-brand secondary so the draft can publish.
        Assert.Matches("^#[0-9a-f]{6}$", applied.Brand.SecondaryColor);
        Assert.NotEqual(applied.Brand.PrimaryColor, applied.Brand.SecondaryColor);
    }

    [Fact]
    public void ApplyLetsExplicitBillerBrandOverrideGeneratedAndResearch()
    {
        // The draft carries non-blank colors/font (as if a model re-emitted its own), and research
        // suggests yet others — the biller's explicit selection must win over both.
        var definition = UnbrandedDefinition() with
        {
            Brand = new ExperienceBrand("City of Vista", "#111111", "#222222", null, "Times")
        };
        var research = ResearchWith(
            (BrandEvidenceFacts.PrimaryColor, "#ff5a1f"),
            (BrandEvidenceFacts.SecondaryColor, "#1e88e5"));
        var biller = Biller() with { Brand = new BillerBrand("#085368", "#18b4e9", null, "Poppins") };

        var applied = ResearchBrandApplicator.Apply(definition, biller, research);

        Assert.Equal("#085368", applied.Brand.PrimaryColor);
        Assert.Equal("#18b4e9", applied.Brand.SecondaryColor);
        Assert.Equal("Poppins", applied.Brand.FontFamily);
    }

    [Fact]
    public void ApplyKeepsExplicitBillerBrandEvenWithoutResearchFacts()
    {
        var definition = UnbrandedDefinition() with
        {
            Brand = new ExperienceBrand("City of Vista", "#111111", "#222222", null, null)
        };
        var research = new BillerResearchResponse(ResearchOutcome.Degraded, [], [], []);
        var biller = Biller() with { Brand = new BillerBrand("#085368", "#18b4e9", null, "Poppins") };

        var applied = ResearchBrandApplicator.Apply(definition, biller, research);

        Assert.Equal("#085368", applied.Brand.PrimaryColor);
        Assert.Equal("#18b4e9", applied.Brand.SecondaryColor);
        Assert.Equal("Poppins", applied.Brand.FontFamily);
    }

    [Fact]
    public void ApplyWithoutFactsReturnsDraftUnchanged()
    {
        var definition = UnbrandedDefinition();
        var research = new BillerResearchResponse(ResearchOutcome.Degraded, [], [], ["research.request_failed"]);

        var applied = ResearchBrandApplicator.Apply(definition, Biller(), research);

        Assert.Same(definition, applied);
    }

    private static string? Value(IReadOnlyList<ResearchFact> facts, string name) =>
        facts.FirstOrDefault(fact => fact.Name == name)?.Value;

    private static BillerResearchResponse ResearchWith(params (string Name, string Value)[] facts) =>
        new(
            ResearchOutcome.Completed,
            facts.Select(fact => new ResearchFact(fact.Name, fact.Value, new Uri("https://vista.example/"), 0.9)).ToArray(),
            [new ResearchSource(new Uri("https://vista.example/"), "Vista", DateTimeOffset.UtcNow)],
            []);

    private static BillerRecord Biller() => new(
        "biller-1", "City of Vista", "city-of-vista", "Utility", "02110",
        new Uri("https://vista.example"), null, null, [], BillerStatus.Prospect, DateTimeOffset.UtcNow);

    private static BillerExperienceDefinition UnbrandedDefinition() => new(
        "1.1",
        "biller-1",
        new ExperienceBrand("City of Vista", string.Empty, string.Empty, null, null),
        new ExperienceContent("Pay your bill", "Welcome", "Support",
            new Uri("https://vista.example/privacy"), new Uri("https://vista.example/terms")),
        new PwaConfiguration("City of Vista", "City", string.Empty, "#FFFFFF", null),
        ["card", "ach"]);
}
