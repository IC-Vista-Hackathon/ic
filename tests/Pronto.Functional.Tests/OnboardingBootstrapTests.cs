using Xunit;

namespace Pronto.Functional.Tests;

/// <summary>
/// The onboarding bootstrap flow that today works and must keep working: creating a biller
/// returns a session in billing discovery, a draft revision, and seeds demo invoices the payer
/// preview can render. Covers FR-1 and FR-2 (see docs/pronto-functional-requirements.md).
/// </summary>
[Trait(Categories.Name, Categories.Functional)]
public sealed class OnboardingBootstrapTests
{
    [SkippableFact]
    public async Task CreatingBillerOpensDiscoveryAndReturnsDraft()
    {
        ProntoEnvironment.RequireBaseUrl();
        using var client = new ProntoApiClient();

        var created = await client.CreateBillerAsync("Functional Test Co", billType: "utility", website: null);

        var billerId = created["biller"]?["biller_id"]?.GetValue<string>();
        Assert.False(string.IsNullOrWhiteSpace(billerId), "create must return a biller_id");
        Assert.Equal("collecting_information", created["session"]?["state"]?.GetValue<string>());
        Assert.NotNull(created["session"]?["current_question"]);
        Assert.Equal("draft", created["draft"]?["state"]?.GetValue<string>());
        Assert.Equal(billerId, created["draft"]?["definition"]?["biller_id"]?.GetValue<string>());
    }

    [SkippableFact]
    public async Task DraftIsRetrievableAfterCreation()
    {
        ProntoEnvironment.RequireBaseUrl();
        using var client = new ProntoApiClient();

        var created = await client.CreateBillerAsync("Functional Test Co", billType: "utility", website: null);
        var billerId = created["biller"]!["biller_id"]!.GetValue<string>();

        var config = await client.GetConfigAsync(billerId);

        Assert.Equal(billerId, config["definition"]?["biller_id"]?.GetValue<string>());
        Assert.NotNull(config["definition"]?["pwa"]);
    }

    [SkippableFact]
    public async Task CreatingBillerSeedsDemoInvoices()
    {
        ProntoEnvironment.RequireBaseUrl();
        using var client = new ProntoApiClient();

        var created = await client.CreateBillerAsync("Functional Test Co", billType: "utility", website: null);
        var billerId = created["biller"]!["biller_id"]!.GetValue<string>();

        var invoices = await client.ListSeededInvoicesAsync(billerId);

        Assert.NotEmpty(invoices);
    }
}
