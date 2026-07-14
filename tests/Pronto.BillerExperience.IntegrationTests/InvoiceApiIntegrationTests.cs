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
public sealed class InvoiceApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private readonly WebApplicationFactory<Program> _factory;

    public InvoiceApiIntegrationTests(WebApplicationFactory<Program> factory) => _factory = factory;

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

    private sealed record SeedRequest(int? Count, string? AccountNumber);

    private sealed record SeedResponse(int Seeded, string AccountNumber);

    private sealed record InvoiceList(IReadOnlyList<InvoiceItem> Invoices);

    private sealed record InvoiceItem(string Id, string AccountNumber);
}
