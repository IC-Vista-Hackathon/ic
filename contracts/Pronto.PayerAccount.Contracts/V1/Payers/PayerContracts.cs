using System.Text.Json.Serialization;

namespace Pronto.PayerAccount.Contracts.V1.Payers;

public sealed record RegisterPayerRequest(
    string BillerId,
    string Name,
    string Email,
    string? Phone,
    IReadOnlyList<string> AccountNumbers,
    PayerPreferences? Preferences = null);

public sealed record PayerResponse(
    string PayerId,
    string BillerId,
    string Name,
    string Email,
    string? Phone,
    IReadOnlyList<string> AccountNumbers,
    PayerPreferences Preferences);

public sealed record PayerPreferences(
    bool Autopay,
    bool Paperless,
    IReadOnlyList<NotificationChannel> Channels,
    int? PaymentDay);

public sealed record UpdatePayerPreferencesRequest(
    bool? Autopay = null,
    bool? Paperless = null,
    IReadOnlyList<NotificationChannel>? Channels = null,
    int? PaymentDay = null);

/// <summary>
/// Link one or more biller account numbers to an existing payer. Linking is idempotent:
/// re-linking accounts the payer already holds is a no-op, and a link already owned by a
/// different payer for the same biller is rejected with 409 <c>account_already_linked</c>.
/// </summary>
public sealed record LinkAccountsRequest(
    IReadOnlyList<string> AccountNumbers);

/// <summary>Wire tokens pinned at the type level so serialization is host-independent.</summary>
[JsonConverter(typeof(NotificationChannelJsonConverter))]
public enum NotificationChannel
{
    [JsonStringEnumMemberName("email")]
    Email,

    [JsonStringEnumMemberName("sms")]
    Sms,
}

/// <summary>String-only converter for <see cref="NotificationChannel"/> (rejects integer tokens).</summary>
public sealed class NotificationChannelJsonConverter : JsonStringEnumConverter<NotificationChannel>
{
    public NotificationChannelJsonConverter()
        : base(namingPolicy: null, allowIntegerValues: false)
    {
    }
}
