using System.Text.Json;
using System.Text.Json.Serialization;
using Pronto.PayerAccount.Contracts.V1.Payers;
using Xunit;

namespace Pronto.PayerAccount.Contracts.Tests;

public sealed class PayerContractsTests
{
    // Wire policy: snake_case + lowercase string enums (design/contracts.md).
    private static readonly JsonSerializerOptions CaseInsensitive = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    [Fact]
    public void PayerResponseRoundTripsThroughJson()
    {
        var response = new PayerResponse(
            PayerId: "f4a8c2e6-1b5d-49f3-8e7a-0c9b6d5e4f31",
            BillerId: "3b7c1d52-88aa-4f0e-bd21-64a9a0f3c111",
            Name: "Brianne Will",
            Email: "brianne@example.com",
            Phone: null,
            AccountNumbers: ["UTIL-1912723364"],
            Preferences: new PayerPreferences(
                Autopay: true,
                Paperless: false,
                Channels: [NotificationChannel.Email, NotificationChannel.Sms],
                PaymentDay: 24));

        var roundTripped = JsonSerializer.Deserialize<PayerResponse>(
            JsonSerializer.Serialize(response, CaseInsensitive), CaseInsensitive);

        Assert.Equal(response.PayerId, roundTripped!.PayerId);
        Assert.Equal(response.AccountNumbers, roundTripped.AccountNumbers);
        // PayerPreferences holds a collection, so record equality is reference-based — compare fields.
        Assert.Equal(response.Preferences.Autopay, roundTripped.Preferences.Autopay);
        Assert.Equal(response.Preferences.Paperless, roundTripped.Preferences.Paperless);
        Assert.Equal(response.Preferences.Channels, roundTripped.Preferences.Channels);
        Assert.Equal(response.Preferences.PaymentDay, roundTripped.Preferences.PaymentDay);
    }

    [Fact]
    public void UpdateRequestDefaultsToNoChanges()
    {
        var request = new UpdatePayerPreferencesRequest();

        Assert.Null(request.Autopay);
        Assert.Null(request.Paperless);
        Assert.Null(request.Channels);
        Assert.Null(request.PaymentDay);
    }

    [Fact]
    public void UpdateRequestDeserializesEmptyBodyAsNoChanges()
    {
        var request = JsonSerializer.Deserialize<UpdatePayerPreferencesRequest>("{}", CaseInsensitive);

        Assert.NotNull(request);
        Assert.Null(request.Autopay);
        Assert.Null(request.Paperless);
        Assert.Null(request.Channels);
        Assert.Null(request.PaymentDay);
    }

    [Fact]
    public void UpdateRequestPreservesPartialChanges()
    {
        const string json = """{"autopay":true,"channels":["email"]}""";

        var request = JsonSerializer.Deserialize<UpdatePayerPreferencesRequest>(json, CaseInsensitive);

        Assert.NotNull(request);
        Assert.True(request.Autopay);
        Assert.Equal([NotificationChannel.Email], request.Channels!);
        Assert.Null(request.Paperless);
        Assert.Null(request.PaymentDay);
    }
}
