using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Pronto.BillerExperience.IntegrationTests;

/// <summary>
/// In-process integration coverage for the Invoice API: boots the real host with
/// <see cref="WebApplicationFactory{TEntryPoint}"/> (controllers, wire policy, error
/// envelope, in-memory store) and exercises the seed -> lookup flow end to end.
///
/// These run in CI on every PR (no external infra). The deployed functional
/// equivalent is scripts/smoke-test.sh, which the nonprod/prod deploy workflows run
/// against a live cluster. New cross-service and Cosmos-backed integration tests
/// should be added here.
/// </summary>
public sealed class InvoiceApiIntegrationTests : IClassFixture<TestingAppFactory>
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private readonly TestingAppFactory _factory;

    public InvoiceApiIntegrationTests(TestingAppFactory factory) => _factory = factory;

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task HealthEndpointsReturnOk(string path)
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SeededInvoicesAreReturnedByAccountLookup()
    {
        var client = _factory.CreateClient();
        const string biller = "integration-biller";
        const string account = "ACME-001";
        var seedBase = $"/billers/{biller}/invoices";

        var seedResponse = await client.PostAsJsonAsync(
            $"{seedBase}/seed",
            new SeedRequest(Count: 3, AccountNumber: account),
            Wire);
        Assert.Equal(HttpStatusCode.Created, seedResponse.StatusCode);

        var seeded = await seedResponse.Content.ReadFromJsonAsync<SeedResponse>(Wire);
        Assert.NotNull(seeded);
        Assert.Equal(3, seeded!.Seeded);
        Assert.Equal(account, seeded.AccountNumber);

        var listResponse = await client.GetAsync($"{seedBase}?account_number={account}");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var list = await listResponse.Content.ReadFromJsonAsync<InvoiceList>(Wire);
        Assert.NotNull(list);
        Assert.Equal(3, list!.Invoices.Count);
        Assert.All(list.Invoices, invoice => Assert.Equal(account, invoice.AccountNumber));
    }

    [Fact]
    public async Task MultipleCategoryInvoicesAreSeededAndReturnedForOneAccount()
    {
        var client = _factory.CreateClient();
        const string biller = "multi-invoice-biller";
        const string account = "4421";
        var seedBase = $"/billers/{biller}/invoices";

        var specs = new[]
        {
            new SeedSpec("Water & sewer", 4200, 14, "Alex Rivera", "Water & sewer", "yellow"),
            new SeedSpec("Stormwater", 3100, 21, "Jordan Chen", "Stormwater", "green"),
            new SeedSpec("Waste collection", 2600, 30, "Sam Okafor", "Waste collection", "green"),
        };

        var seedResponse = await client.PostAsJsonAsync(
            $"{seedBase}/seed", new SeedRequest(Count: null, AccountNumber: account, Invoices: specs), Wire);
        Assert.Equal(HttpStatusCode.Created, seedResponse.StatusCode);

        var list = await client.GetFromJsonAsync<InvoiceList>($"{seedBase}?account_number={account}", Wire);
        Assert.NotNull(list);
        Assert.Equal(3, list!.Invoices.Count);
    }

    [Fact]
    public async Task ReSeedingTheSameSetDoesNotDuplicateInvoices()
    {
        var client = _factory.CreateClient();
        const string biller = "reseed-biller";
        const string account = "4421";
        var seedBase = $"/billers/{biller}/invoices";
        var request = new SeedRequest(Count: 4, AccountNumber: account, Invoices: null);

        await client.PostAsJsonAsync($"{seedBase}/seed", request, Wire);
        await client.PostAsJsonAsync($"{seedBase}/seed", request, Wire);

        // Deterministic ids + upsert: re-publishing the same seed set must not duplicate invoices.
        var list = await client.GetFromJsonAsync<InvoiceList>($"{seedBase}?account_number={account}", Wire);
        Assert.NotNull(list);
        Assert.Equal(4, list!.Invoices.Count);
    }

    [Fact]
    public async Task ReSeedingWithAChangedProfileReplacesRatherThanAccumulates()
    {
        var client = _factory.CreateClient();
        const string biller = "changed-profile-biller";
        const string account = "4421";
        var seedBase = $"/billers/{biller}/invoices";

        // Mirrors create (assumed profile) then publish (finalized, different categories): the same
        // number of slots but different descriptions. The account must reflect only the latest set.
        var assumed = new[]
        {
            new SeedSpec("Monthly statement", 4200, 14, "Alex Rivera", "General", "green"),
            new SeedSpec("Service charge", 3100, 21, "Jordan Chen", "General", "green"),
        };
        var finalized = new[]
        {
            new SeedSpec("Water & sewer", 5200, 14, "Alex Rivera", "Water & sewer", "yellow"),
            new SeedSpec("Stormwater", 2600, 21, "Jordan Chen", "Stormwater", "green"),
        };

        await client.PostAsJsonAsync($"{seedBase}/seed", new SeedRequest(null, account, assumed), Wire);
        await client.PostAsJsonAsync($"{seedBase}/seed", new SeedRequest(null, account, finalized), Wire);

        var list = await client.GetFromJsonAsync<InvoiceList>($"{seedBase}?account_number={account}", Wire);
        Assert.NotNull(list);
        Assert.Equal(2, list!.Invoices.Count);
        Assert.All(list.Invoices, invoice => Assert.DoesNotContain("statement", invoice.Description));
    }

    [Fact]
    public async Task ReSeedingWithFewerInvoicesDropsTheEarlierExtraSlots()
    {
        var client = _factory.CreateClient();
        const string biller = "shrinking-profile-biller";
        const string account = "4421";
        var seedBase = $"/billers/{biller}/invoices";

        // First publish: five categories → five slots. A later re-publish (e.g. requeuing a failed
        // deployment after the biller reduced categories) yields only two. The account must not
        // retain the three shrunk-away slots.
        var larger = new[]
        {
            new SeedSpec("Water & sewer", 5200, 14, "Alex Rivera", "Water & sewer", "yellow"),
            new SeedSpec("Stormwater", 2600, 21, "Jordan Chen", "Stormwater", "green"),
            new SeedSpec("Waste collection", 3100, 30, "Sam Okafor", "Waste collection", "green"),
            new SeedSpec("Recycling", 1500, 30, "Sam Okafor", "Recycling", "green"),
            new SeedSpec("Street lighting", 900, 30, "Sam Okafor", "Street lighting", "green"),
        };
        var smaller = new[]
        {
            new SeedSpec("Water & sewer", 5200, 14, "Alex Rivera", "Water & sewer", "yellow"),
            new SeedSpec("Stormwater", 2600, 21, "Jordan Chen", "Stormwater", "green"),
        };

        await client.PostAsJsonAsync($"{seedBase}/seed", new SeedRequest(null, account, larger), Wire);
        await client.PostAsJsonAsync($"{seedBase}/seed", new SeedRequest(null, account, smaller), Wire);

        var list = await client.GetFromJsonAsync<InvoiceList>($"{seedBase}?account_number={account}", Wire);
        Assert.NotNull(list);
        Assert.Equal(2, list!.Invoices.Count);
        Assert.All(list.Invoices, invoice => Assert.DoesNotContain("Waste", invoice.Description));
    }

    private sealed record SeedRequest(int? Count, string? AccountNumber, IReadOnlyList<SeedSpec>? Invoices = null);

    private sealed record SeedSpec(
        string Description, int AmountCents, int DueInDays, string? PayerName, string? Type, string? StatusColor);

    private sealed record SeedResponse(int Seeded, string AccountNumber);

    private sealed record InvoiceList(IReadOnlyList<InvoiceItem> Invoices);

    private sealed record InvoiceItem(string Id, string AccountNumber, string Description);
}
