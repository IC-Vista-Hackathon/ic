using System.Text.Json.Nodes;
using Xunit;

namespace Pronto.Functional.Tests;

/// <summary>
/// FR-3 — The biller research agent must scrape the biller's own website and pull real brand
/// evidence (logo, colors, styling, copy). FR-4 — that evidence must flow into the draft the
/// biller previews.
///
/// KNOWN GAP: today research is skipped/deterministic (the crawler only extracts page title and
/// meta description — no logo/colors/styling), so the draft keeps its placeholder brand and no
/// research agent runs to completion. Example biller sites that SHOULD yield brand evidence:
/// https://www.happypantsnyc.com and https://thankful-sea-0d2febf0f.7.azurestaticapps.net .
/// See docs/pronto-functional-requirements.md (FR-3, FR-4).
/// </summary>
[Trait(Categories.Name, Categories.Functional)]
[Trait(Categories.Name, Categories.KnownGap)]
public sealed class BrandResearchTests
{
    private const string FabricatedDefaultPrimaryColor = "#085368";
    private const string ExampleBillerSite = "https://www.happypantsnyc.com";

    [SkippableFact]
    public async Task ResearchAgentRunsToCompletionForReachableSite()
    {
        ProntoEnvironment.RequireBaseUrl();
        using var client = new ProntoApiClient();

        var created = await client.CreateBillerAsync("Happy Pants NYC", billType: "other", website: ExampleBillerSite);
        var billerId = created["biller"]!["biller_id"]!.GetValue<string>();

        // A chat turn kicks the onboarding orchestration, which includes the research step.
        await client.SendChatAsync(billerId, "Use our real brand from our website for the preview.");

        var events = await PollActivityAsync(
            client,
            billerId,
            predicate: activity => activity.Any(IsSuccessfulResearchEvent));

        Assert.True(events.Count > 0, "No agent activity was recorded for the onboarding run.");
        Assert.Contains(events, IsSuccessfulResearchEvent);
    }

    [SkippableFact]
    public async Task OnboardingDerivesBrandIdentityFromBillerSite()
    {
        ProntoEnvironment.RequireBaseUrl();
        using var client = new ProntoApiClient();

        var created = await client.CreateBillerAsync("Happy Pants NYC", billType: "other", website: ExampleBillerSite);
        var billerId = created["biller"]!["biller_id"]!.GetValue<string>();

        await client.SendChatAsync(billerId, "Use our real brand from our website for the preview.");

        // Give research + draft regeneration time to update the brand.
        var brand = await PollBrandAsync(
            client,
            billerId,
            predicate: b => b?["logo_asset_id"].AsStringOrNull() is not null);

        var logo = brand?["logo_asset_id"].AsStringOrNull();
        var primaryColor = brand?["primary_color"].AsStringOrNull();

        Assert.False(string.IsNullOrEmpty(logo),
            "Draft has no logo_asset_id; research did not pull a logo from the biller site.");
        Assert.NotEqual(FabricatedDefaultPrimaryColor, primaryColor);
    }

    private static bool IsSuccessfulResearchEvent(JsonNode activityEvent)
    {
        var agentId = activityEvent["agent_id"].AsStringOrNull() ?? string.Empty;
        var status = activityEvent["status"].AsStringOrNull() ?? string.Empty;
        var isResearch = agentId.Contains("research", StringComparison.OrdinalIgnoreCase);
        return isResearch && status is "completed" or "degraded";
    }

    private static async Task<IReadOnlyList<JsonNode>> PollActivityAsync(
        ProntoApiClient client,
        string billerId,
        Func<IReadOnlyList<JsonNode>, bool> predicate)
    {
        IReadOnlyList<JsonNode> events = [];
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var snapshot = await client.GetActivityAsync(billerId);
            events = snapshot["activity"]?.AsArray().OfType<JsonNode>().ToArray() ?? [];
            if (predicate(events))
            {
                return events;
            }

            await Task.Delay(TimeSpan.FromSeconds(3));
        }

        return events;
    }

    private static async Task<JsonNode?> PollBrandAsync(
        ProntoApiClient client,
        string billerId,
        Func<JsonNode?, bool> predicate)
    {
        JsonNode? brand = null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var config = await client.GetConfigAsync(billerId);
            brand = config["definition"]?["brand"];
            if (predicate(brand))
            {
                return brand;
            }

            await Task.Delay(TimeSpan.FromSeconds(3));
        }

        return brand;
    }
}
