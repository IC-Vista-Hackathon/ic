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

public enum NotificationChannel
{
    Email,
    Sms
}
