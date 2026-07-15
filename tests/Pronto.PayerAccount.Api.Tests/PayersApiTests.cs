using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pronto.PayerAccount.Contracts.V1.Payers;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Pronto.PayerAccount.Api.Tests;

public sealed class PayersApiTests : IClassFixture<TestingAppFactory>
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private readonly HttpClient client;

    public PayersApiTests(TestingAppFactory factory)
    {
        client = factory.CreateClient();
    }

    [Fact]
    public async Task RegisterThenGetReturnsDefaultsOffPreferences()
    {
        var billerId = Guid.NewGuid().ToString();

        var response = await client.PostAsJsonAsync(
            "payers",
            new RegisterPayerRequest(billerId, "Brianne Will", "brianne@example.com", null, ["ACCT-1"]),
            Wire);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payer = await response.Content.ReadFromJsonAsync<PayerResponse>(Wire);
        Assert.NotNull(payer);
        Assert.False(payer.Preferences.Autopay);
        Assert.False(payer.Preferences.Paperless);
        Assert.Empty(payer.Preferences.Channels);
        Assert.Null(payer.Preferences.PaymentDay);

        var fetched = await client.GetFromJsonAsync<PayerResponse>(
            $"payers/{payer.PayerId}?biller_id={billerId}", Wire);
        Assert.Equal(payer.PayerId, fetched!.PayerId);
    }

    [Fact]
    public async Task DuplicateEmailConflictsCaseInsensitively()
    {
        var billerId = Guid.NewGuid().ToString();
        await client.PostAsJsonAsync(
            "payers",
            new RegisterPayerRequest(billerId, "A", "same@example.com", null, []),
            Wire);

        var duplicate = await client.PostAsJsonAsync(
            "payers",
            new RegisterPayerRequest(billerId, "B", "  SAME@EXAMPLE.COM ", null, []),
            Wire);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Contains(
            "already_registered", await duplicate.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnablingAutopayWithoutDayRejected()
    {
        var payer = await RegisterPayerAsync();

        var response = await client.PatchAsJsonAsync(
            $"payers/{payer.PayerId}/preferences?biller_id={payer.BillerId}",
            new UpdatePayerPreferencesRequest(Autopay: true),
            Wire);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "autopay_requires_payment_day",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnablingAutopayWithDaySucceedsAndMergesPartially()
    {
        var payer = await RegisterPayerAsync();

        var enable = await client.PatchAsJsonAsync(
            $"payers/{payer.PayerId}/preferences?biller_id={payer.BillerId}",
            new UpdatePayerPreferencesRequest(Autopay: true, PaymentDay: 24),
            Wire);
        Assert.Equal(HttpStatusCode.OK, enable.StatusCode);

        // Partial PATCH: only paperless — autopay/day untouched.
        var partial = await client.PatchAsJsonAsync(
            $"payers/{payer.PayerId}/preferences?biller_id={payer.BillerId}",
            new UpdatePayerPreferencesRequest(Paperless: true),
            Wire);
        var preferences = await partial.Content.ReadFromJsonAsync<PayerPreferences>(Wire);

        Assert.True(preferences!.Autopay);
        Assert.True(preferences.Paperless);
        Assert.Equal(24, preferences.PaymentDay);
    }

    [Fact]
    public async Task RegisterWithPreferencesAppliesThem()
    {
        var billerId = Guid.NewGuid().ToString();

        var response = await client.PostAsJsonAsync(
            "payers",
            new RegisterPayerRequest(
                billerId, "Pref Payer", $"{Guid.NewGuid()}@example.com", null, [],
                new PayerPreferences(
                    Autopay: true, Paperless: true, Channels: [NotificationChannel.Email], PaymentDay: 15)),
            Wire);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payer = await response.Content.ReadFromJsonAsync<PayerResponse>(Wire);
        Assert.True(payer!.Preferences.Autopay);
        Assert.Equal(15, payer.Preferences.PaymentDay);
    }

    [Fact]
    public async Task RegisterWithAutopayButNoDayRejected()
    {
        var response = await client.PostAsJsonAsync(
            "payers",
            new RegisterPayerRequest(
                Guid.NewGuid().ToString(), "Pref Payer", $"{Guid.NewGuid()}@example.com", null, [],
                new PayerPreferences(Autopay: true, Paperless: false, Channels: [], PaymentDay: null)),
            Wire);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "autopay_requires_payment_day",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PaymentDayOutOfRangeRejected()
    {
        var payer = await RegisterPayerAsync();

        var response = await client.PatchAsJsonAsync(
            $"payers/{payer.PayerId}/preferences?biller_id={payer.BillerId}",
            new UpdatePayerPreferencesRequest(PaymentDay: 31),
            Wire);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "invalid_payment_day", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PayerCanBeFoundByBillerAccountNumber()
    {
        var payer = await RegisterPayerAsync("LOOKUP-4421");

        var found = await client.GetFromJsonAsync<PayerResponse>(
            $"payers?biller_id={payer.BillerId}&account_number=LOOKUP-4421", Wire);

        Assert.Equal(payer.PayerId, found!.PayerId);
    }

    private async Task<PayerResponse> RegisterPayerAsync(string? accountNumber = null)
    {
        var billerId = Guid.NewGuid().ToString();
        var response = await client.PostAsJsonAsync(
            "payers",
            new RegisterPayerRequest(
                billerId, "Test Payer", $"{Guid.NewGuid()}@example.com", null,
                accountNumber is null ? [] : [accountNumber]),
            Wire);
        return (await response.Content.ReadFromJsonAsync<PayerResponse>(Wire))!;
    }
}
