using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pronto.PayerAccount.Contracts.V1.Payers;
using Xunit;

namespace Pronto.BillerExperience.IntegrationTests;

/// <summary>
/// In-process coverage for the payer half of the onboarding seed path: a demo payer registered
/// against the real PayerAccount host is resolvable by the same (biller, account) the invoices are
/// seeded under, and re-seeding the same payer is idempotent (the service rejects the duplicate,
/// leaving a single payer) — so re-publishing never creates a second demo payer.
/// </summary>
public sealed class PayerSeedIntegrationTests : IClassFixture<PayerAccountAppFactory>
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private readonly PayerAccountAppFactory _factory;

    public PayerSeedIntegrationTests(PayerAccountAppFactory factory) => _factory = factory;

    private static RegisterPayerRequest DemoPayer(string biller, string account) => new(
        biller,
        "Alex Rivera",
        $"demo.payer.{biller}@pronto-demo.example",
        Phone: null,
        AccountNumbers: [account],
        Preferences: new PayerPreferences(
            Autopay: false, Paperless: false, Channels: [NotificationChannel.Email], PaymentDay: null));

    [Fact]
    public async Task SeededPayerIsResolvedByBillerAccountLookup()
    {
        var client = _factory.CreateClient();
        const string biller = "seed-biller-a";
        const string account = "4421";

        var seed = await client.PostAsJsonAsync("payers", DemoPayer(biller, account), Wire);
        Assert.Equal(HttpStatusCode.Created, seed.StatusCode);
        var created = await seed.Content.ReadFromJsonAsync<PayerResponse>(Wire);

        var found = await client.GetFromJsonAsync<PayerResponse>(
            $"payers?biller_id={biller}&account_number={account}", Wire);

        Assert.NotNull(found);
        Assert.Equal(created!.PayerId, found!.PayerId);
        Assert.Contains(account, found.AccountNumbers);
    }

    [Fact]
    public async Task ReSeedingTheSamePayerIsIdempotent()
    {
        var client = _factory.CreateClient();
        const string biller = "seed-biller-b";
        const string account = "4421";

        var first = await client.PostAsJsonAsync("payers", DemoPayer(biller, account), Wire);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstPayer = await first.Content.ReadFromJsonAsync<PayerResponse>(Wire);

        // The seeder maps this conflict to a successful no-op; the service must reject the duplicate
        // rather than create a second payer.
        var second = await client.PostAsJsonAsync("payers", DemoPayer(biller, account), Wire);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var found = await client.GetFromJsonAsync<PayerResponse>(
            $"payers?biller_id={biller}&account_number={account}", Wire);
        Assert.Equal(firstPayer!.PayerId, found!.PayerId);
    }
}
