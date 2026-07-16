using Xunit;

namespace Pronto.Functional.Tests;

/// <summary>
/// FR-5 — Brand identity must be researched before it is presented. The onboarding bootstrap draft
/// (returned by POST /billers, before any research or chat has run) must NOT assert a fabricated
/// brand color or design brief. It should stay unbranded until the biller research agent produces
/// evidence from the biller's real site.
///
/// KNOWN GAP: today CreateInitialDefinition fills the draft with a hard-coded default color
/// (#085368) and a generic design brief at creation time, so these fail until the flow is
/// reordered to research first. See docs/pronto-functional-requirements.md (FR-5).
/// </summary>
[Trait(Categories.Name, Categories.Functional)]
[Trait(Categories.Name, Categories.KnownGap)]
public sealed class PrematureBrandingTests
{
    // The hard-coded placeholder the initial draft currently ships. Presenting it as the biller's
    // brand before research is exactly the behavior FR-5 forbids.
    private const string FabricatedDefaultPrimaryColor = "#085368";

    [SkippableFact]
    public async Task BootstrapDraftDoesNotAssertBrandColorBeforeResearch()
    {
        ProntoEnvironment.RequireBaseUrl();
        using var client = new ProntoApiClient();

        var created = await client.CreateBillerAsync(
            "Happy Pants NYC", billType: "other", website: "https://www.happypantsnyc.com");

        var primaryColor = created["draft"]?["definition"]?["brand"]?["primary_color"].AsStringOrNull();

        Assert.True(
            string.IsNullOrEmpty(primaryColor),
            $"Bootstrap draft presented brand color '{primaryColor}' before any research ran; " +
            "brand should be unset until the research agent produces evidence.");
        Assert.NotEqual(FabricatedDefaultPrimaryColor, primaryColor);
    }

    [SkippableFact]
    public async Task BootstrapDraftDoesNotFabricateDesignBriefBeforeResearch()
    {
        ProntoEnvironment.RequireBaseUrl();
        using var client = new ProntoApiClient();

        var created = await client.CreateBillerAsync(
            "Happy Pants NYC", billType: "other", website: "https://www.happypantsnyc.com");

        var brief = created["draft"]?["definition"]?["brief"];

        Assert.True(
            brief is null,
            "Bootstrap draft shipped a design brief before research; the creative brief must be " +
            "derived from researched brand evidence, not invented at creation time.");
    }
}
