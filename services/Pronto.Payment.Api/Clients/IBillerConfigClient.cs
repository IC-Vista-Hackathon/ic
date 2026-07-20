using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pronto.ServiceDefaults;

namespace Pronto.Payment.Api.Clients;

/// <summary>
/// Fee/receipt + payment-policy slice of BillerConfiguration the Payment Service needs. The
/// installment/partial fields are the biller's <em>policy</em>: the server validates every
/// requested amount and plan against them, so a client can never pay outside what the biller
/// allows regardless of what the UI offers.
/// </summary>
public sealed record BillerPaymentConfig(
    IReadOnlyList<string> PaymentMethods,
    decimal CardPercent,
    int AchFlatCents,
    bool PayerPaysFee,
    string ReceiptMessage,
    BillerSettlementState SettlementState,
    bool PartialPaymentsAllowed = false,
    int MinPartialPaymentCents = 0,
    bool InstallmentsAllowed = false,
    int MaxInstallments = 0);

/// <summary>
/// Whether a biller's configuration has cleared the publish + compliance gate and is therefore
/// allowed to settle real payments. This is server-owned and derived from the biller's published
/// configuration and compliance review — it is NEVER agent- or client-writable, so no request field
/// can move a biller into a settle-eligible state.
/// </summary>
public enum BillerSettlementState
{
    /// <summary>Configuration has not been published; no payments may settle.</summary>
    Unpublished,

    /// <summary>Configuration is published but has not passed the compliance gate.</summary>
    ComplianceNotPassed,

    /// <summary>Published and compliance-approved — the only settle-eligible state.</summary>
    Published,
}

public interface IBillerConfigClient
{
    Task<BillerPaymentConfig> GetAsync(string billerId, CancellationToken cancellationToken);
}

/// <summary>
/// Demo defaults. BillerExperience contracts don't expose fees yet (flagged to that team);
/// swap this for an HTTP client when their config read endpoint lands. The demo biller is treated
/// as published + compliance-approved so local/preview flows can settle against fake rails.
/// </summary>
public sealed class DemoBillerConfigClient : IBillerConfigClient
{
    internal static readonly BillerPaymentConfig Default = new(
        PaymentMethods: ["card", "ach", "applepay", "googlepay", "paypal"],
        CardPercent: 2.5m,
        AchFlatCents: 150,
        PayerPaysFee: true,
        ReceiptMessage: "Thank you for your payment!",
        SettlementState: BillerSettlementState.Published,
        PartialPaymentsAllowed: true,
        MinPartialPaymentCents: 500,
        InstallmentsAllowed: true,
        MaxInstallments: 12);

    public Task<BillerPaymentConfig> GetAsync(string billerId, CancellationToken cancellationToken)
        => Task.FromResult(Default);
}

/// <summary>
/// Reads the biller's fee policy from the Biller Experience config read endpoint and maps
/// <c>fee_handling</c> onto whether the payer is charged the service fee, so a biller that absorbs
/// fees never has one added to the payer's server-side quote/total. Everything else stays on the
/// demo policy defaults. Preview tenants (<c>preview-{id}</c>) resolve their shadowed live biller's
/// config. Any failure falls back to <see cref="DemoBillerConfigClient.Default"/> so a config read
/// outage can never block a payment — it just reverts to today's behavior.
/// </summary>
public sealed class HttpBillerConfigClient(HttpClient http, ILogger<HttpBillerConfigClient> logger)
    : IBillerConfigClient
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public async Task<BillerPaymentConfig> GetAsync(string billerId, CancellationToken cancellationToken)
    {
        var liveBillerId = PreviewTenant.LiveBillerId(billerId);
        try
        {
            using var response = await http.GetAsync(
                new Uri($"billers/{Uri.EscapeDataString(liveBillerId)}/config", UriKind.Relative),
                cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return DemoBillerConfigClient.Default;
            }
            response.EnsureSuccessStatusCode();
            var envelope = await response.Content.ReadFromJsonAsync<ConfigEnvelope>(Wire, cancellationToken);
            var feeHandling = envelope?.Definition?.Preferences?.FeeHandling;
            if (feeHandling is null)
            {
                return DemoBillerConfigClient.Default;
            }
            return DemoBillerConfigClient.Default with { PayerPaysFee = PayerIsCharged(feeHandling) };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogConfigReadFailed(logger, liveBillerId, exception);
            return DemoBillerConfigClient.Default;
        }
    }

    // "charge"/"mixed" pass the service fee to the payer; "absorb"/"undecided" (and anything
    // unrecognized) do not, matching the server-side compliance fee-disclosure policy.
    private static bool PayerIsCharged(string feeHandling) =>
        string.Equals(feeHandling, "charge", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(feeHandling, "mixed", StringComparison.OrdinalIgnoreCase);

    private sealed record ConfigEnvelope(DefinitionEnvelope? Definition);
    private sealed record DefinitionEnvelope(PreferencesEnvelope? Preferences);
    private sealed record PreferencesEnvelope(
        [property: JsonPropertyName("fee_handling")] string? FeeHandling);

    private static readonly Action<ILogger, string, Exception> LogConfigReadFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2100, nameof(HttpBillerConfigClient)),
            "Could not read fee policy for biller {BillerId}; using demo defaults.");
}
