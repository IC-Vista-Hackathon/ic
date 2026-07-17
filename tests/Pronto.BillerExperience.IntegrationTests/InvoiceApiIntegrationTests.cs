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
    public async Task PreviewReseedWithReplaceIsDeterministicAndDoesNotAccumulate()
    {
        var client = _factory.CreateClient();
        // preview- marks an isolated preview tenant; only these honor replace.
        const string biller = "preview-integration-biller";
        const string account = "4421";
        var seedBase = $"/billers/{biller}/invoices";

        for (var run = 0; run < 3; run++)
        {
            var response = await client.PostAsJsonAsync(
                $"{seedBase}/seed", new SeedRequest(Count: 4, AccountNumber: account, Replace: true), Wire);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        var list = await (await client.GetAsync($"{seedBase}?account_number={account}"))
            .Content.ReadFromJsonAsync<InvoiceList>(Wire);
        // Three replace-mode re-seeds leave exactly one seed set — a reset wipes, not accumulates.
        Assert.NotNull(list);
        Assert.Equal(4, list!.Invoices.Count);
    }

    [Fact]
    public async Task ReplaceIsIgnoredForLiveBillersSoRealDataIsNeverWiped()
    {
        var client = _factory.CreateClient();
        // A live (non-preview) biller: replace must be a no-op so real invoices are never purged.
        const string biller = "live-integration-biller";
        const string account = "LIVE-77";
        var seedBase = $"/billers/{biller}/invoices";

        await client.PostAsJsonAsync($"{seedBase}/seed", new SeedRequest(2, account, Replace: true), Wire);
        await client.PostAsJsonAsync($"{seedBase}/seed", new SeedRequest(2, account, Replace: true), Wire);

        var list = await (await client.GetAsync($"{seedBase}?account_number={account}"))
            .Content.ReadFromJsonAsync<InvoiceList>(Wire);
        // Both seeds appended (replace ignored) — live billers keep their existing invoices.
        Assert.NotNull(list);
        Assert.Equal(4, list!.Invoices.Count);
    }

    private sealed record SeedRequest(int? Count, string? AccountNumber, bool Replace = false);

    private sealed record SeedResponse(int Seeded, string AccountNumber);

    private sealed record InvoiceList(IReadOnlyList<InvoiceItem> Invoices);

    private sealed record InvoiceItem(string Id, string AccountNumber);
}
