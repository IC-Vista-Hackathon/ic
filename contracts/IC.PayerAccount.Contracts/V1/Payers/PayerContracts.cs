using System.Text.Json.Serialization;

namespace IC.PayerAccount.Contracts.V1.Payers;

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
