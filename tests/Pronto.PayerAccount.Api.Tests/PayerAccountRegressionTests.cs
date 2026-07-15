using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pronto.PayerAccount.Contracts.V1.Payers;
using Xunit;

namespace Pronto.PayerAccount.Api.Tests;

/// <summary>
/// Regression coverage for the audited PayerAccount fixes: account-link ownership, unique and
/// idempotent linking, notification-channel validation, and lost-update-free preference PATCH.
/// </summary>
public sealed class PayerAccountRegressionTests
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private static readonly string[] ExpectedAAndB = ["A-1", "B-2"];

    [Fact]
    public async Task RegisterWithUnownedAccountRejected()
    {
        using var factory = new PayerAccountApiFactory { Ownership = (_, account) => account != "GHOST" };
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "payers",
            new RegisterPayerRequest(
                Guid.NewGuid().ToString(), "Nova", $"{Guid.NewGuid()}@example.com", null, ["GHOST"]),
            Wire);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("account_not_owned", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DuplicateAccountLinkAcrossPayersRejected()
    {
        using var factory = new PayerAccountApiFactory();
        var client = factory.CreateClient();
        var billerId = Guid.NewGuid().ToString();

        var first = await client.PostAsJsonAsync(
            "payers",
            new RegisterPayerRequest(billerId, "First", $"{Guid.NewGuid()}@example.com", null, ["SHARED-1"]),
            Wire);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync(
            "payers",
            new RegisterPayerRequest(billerId, "Second", $"{Guid.NewGuid()}@example.com", null, ["SHARED-1"]),
            Wire);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("account_already_linked", await second.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LinkAccountsIsIdempotentAndUnions()
    {
        using var factory = new PayerAccountApiFactory();
        var client = factory.CreateClient();
        var payer = await RegisterAsync(client, ["A-1"]);

        var linked = await client.PostAsJsonAsync(
            $"payers/{payer.PayerId}/accounts?biller_id={payer.BillerId}",
            new LinkAccountsRequest(["A-1", "B-2"]),
            Wire);
        Assert.Equal(HttpStatusCode.OK, linked.StatusCode);
        var afterFirst = await linked.Content.ReadFromJsonAsync<PayerResponse>(Wire);
        Assert.Equal(ExpectedAAndB, afterFirst!.AccountNumbers);

        // Re-linking an account the payer already holds is a no-op, not a conflict.
        var again = await client.PostAsJsonAsync(
            $"payers/{payer.PayerId}/accounts?biller_id={payer.BillerId}",
            new LinkAccountsRequest(["B-2"]),
            Wire);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var afterSecond = await again.Content.ReadFromJsonAsync<PayerResponse>(Wire);
        Assert.Equal(ExpectedAAndB, afterSecond!.AccountNumbers);

        var found = await client.GetFromJsonAsync<PayerResponse>(
            $"payers?biller_id={payer.BillerId}&account_number=B-2", Wire);
        Assert.Equal(payer.PayerId, found!.PayerId);
    }

    [Fact]
    public async Task LinkAccountOwnedByAnotherPayerRejected()
    {
        using var factory = new PayerAccountApiFactory();
        var client = factory.CreateClient();
        var billerId = Guid.NewGuid().ToString();

        var owner = await RegisterAsync(client, ["OWNED-9"], billerId);
        var other = await RegisterAsync(client, [], billerId);

        var response = await client.PostAsJsonAsync(
            $"payers/{other.PayerId}/accounts?biller_id={billerId}",
            new LinkAccountsRequest(["OWNED-9"]),
            Wire);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("account_already_linked", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.NotEqual(owner.PayerId, other.PayerId);
    }

    [Fact]
    public async Task LinkAccountsToUnknownPayerReturnsNotFound()
    {
        using var factory = new PayerAccountApiFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"payers/{Guid.NewGuid()}/accounts?biller_id={Guid.NewGuid()}",
            new LinkAccountsRequest(["Z-1"]),
            Wire);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task LinkUnownedAccountRejected()
    {
        using var factory = new PayerAccountApiFactory { Ownership = (_, account) => account != "GHOST" };
        var client = factory.CreateClient();
        var payer = await RegisterAsync(client, []);

        var response = await client.PostAsJsonAsync(
            $"payers/{payer.PayerId}/accounts?biller_id={payer.BillerId}",
            new LinkAccountsRequest(["GHOST"]),
            Wire);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("account_not_owned", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RegisterWithInvalidEmailRejected()
    {
        using var factory = new PayerAccountApiFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "payers",
            new RegisterPayerRequest(Guid.NewGuid().ToString(), "Bad Email", "not-an-email", null, []),
            Wire);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("invalid_email", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SmsChannelWithoutPhoneRejectedOnRegister()
    {
        using var factory = new PayerAccountApiFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "payers",
            new RegisterPayerRequest(
                Guid.NewGuid().ToString(), "No Phone", $"{Guid.NewGuid()}@example.com", null, [],
                new PayerPreferences(Autopay: false, Paperless: false, Channels: [NotificationChannel.Sms], PaymentDay: null)),
            Wire);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("sms_channel_requires_phone", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SmsChannelWithPhoneAccepted()
    {
        using var factory = new PayerAccountApiFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "payers",
            new RegisterPayerRequest(
                Guid.NewGuid().ToString(), "Has Phone", $"{Guid.NewGuid()}@example.com", "+1-555-0100", [],
                new PayerPreferences(Autopay: false, Paperless: false, Channels: [NotificationChannel.Sms], PaymentDay: null)),
            Wire);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payer = await response.Content.ReadFromJsonAsync<PayerResponse>(Wire);
        Assert.Contains(NotificationChannel.Sms, payer!.Preferences.Channels);
    }

    [Fact]
    public async Task AddingSmsChannelViaPatchWithoutPhoneRejected()
    {
        using var factory = new PayerAccountApiFactory();
        var client = factory.CreateClient();
        var payer = await RegisterAsync(client, []); // registered without a phone

        var response = await client.PatchAsJsonAsync(
            $"payers/{payer.PayerId}/preferences?biller_id={payer.BillerId}",
            new UpdatePayerPreferencesRequest(Channels: [NotificationChannel.Sms]),
            Wire);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("sms_channel_requires_phone", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConcurrentPreferencePatchesDoNotLoseUpdates()
    {
        using var factory = new PayerAccountApiFactory();
        var client = factory.CreateClient();
        var payer = await RegisterAsync(client, []);

        // Fire independent field updates concurrently; every one must survive the merge.
        var paperlessTask = client.PatchAsJsonAsync(
            $"payers/{payer.PayerId}/preferences?biller_id={payer.BillerId}",
            new UpdatePayerPreferencesRequest(Paperless: true), Wire);
        var autopayTask = client.PatchAsJsonAsync(
            $"payers/{payer.PayerId}/preferences?biller_id={payer.BillerId}",
            new UpdatePayerPreferencesRequest(Autopay: true, PaymentDay: 12), Wire);

        var responses = await Task.WhenAll(paperlessTask, autopayTask);
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));

        var final = await client.GetFromJsonAsync<PayerResponse>(
            $"payers/{payer.PayerId}?biller_id={payer.BillerId}", Wire);
        Assert.True(final!.Preferences.Paperless);
        Assert.True(final.Preferences.Autopay);
        Assert.Equal(12, final.Preferences.PaymentDay);
    }

    private static async Task<PayerResponse> RegisterAsync(
        HttpClient client, IReadOnlyList<string> accounts, string? billerId = null)
    {
        var response = await client.PostAsJsonAsync(
            "payers",
            new RegisterPayerRequest(
                billerId ?? Guid.NewGuid().ToString(),
                "Test Payer",
                $"{Guid.NewGuid()}@example.com",
                null,
                accounts),
            Wire);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PayerResponse>(Wire))!;
    }
}
