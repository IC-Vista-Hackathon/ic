using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pronto.PayerAccount.Contracts.V1.Payers;

namespace Pronto.BillerExperience.Api.Infrastructure.SupportingServices;

/// <summary>
/// Seeds the demo payer into the PayerAccount service over HTTP, mirroring
/// <see cref="HttpInvoiceSeeder"/>: it picks a deterministic demo payer, POSTs it to
/// <c>/payers</c>, and propagates the correlation/biller headers. Idempotency is inherent — the
/// PayerAccount service rejects a duplicate per-biller email or an already-linked account with
/// <c>409 Conflict</c>, which this seeder treats as a successful no-op so re-publishing never
/// creates a second demo payer.
/// </summary>
public sealed partial class HttpPayerSeeder(
    HttpClient http,
    ISeedPayerGenerator generator,
    ILogger<HttpPayerSeeder> logger) : IPayerSeeder
{
    private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) },
    };

    public async ValueTask SeedAsync(
        SeedBillerContext biller,
        IReadOnlyList<string> accountNumbers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(biller);
        ArgumentNullException.ThrowIfNull(accountNumbers);
        var billerId = biller.BillerId;

        // Agent configures: choose the biller-relevant demo payer here; the PayerAccount service
        // verifies account ownership (against seeded invoices) and persists.
        var spec = generator.Generate(biller);
        var request = new RegisterPayerRequest(
            billerId,
            spec.Name,
            spec.Email,
            spec.Phone,
            accountNumbers,
            new PayerPreferences(spec.Autopay, spec.Paperless, MapChannels(spec.Channels), spec.PaymentDay));

        var correlationId = Activity.Current?.GetTagItem("ic.correlation_id")?.ToString();
        using var activity = BillerExperienceTelemetry.Source.StartActivity("payer.seed");
        activity?.SetTag("ic.biller_id", billerId);
        if (!string.IsNullOrWhiteSpace(correlationId)) activity?.SetTag("ic.correlation_id", correlationId);

        try
        {
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using var message = new HttpRequestMessage(HttpMethod.Post, "payers")
                    {
                        Content = JsonContent.Create(request, options: WireOptions)
                    };
                    using var response = await http.SendAsync(message, cancellationToken);

                    // The demo payer already exists (duplicate email or already-linked account):
                    // seeding is idempotent, so a conflict is a successful no-op, not an error.
                    if (response.StatusCode == HttpStatusCode.Conflict)
                    {
                        LogAlreadySeeded(logger, billerId, Activity.Current?.TraceId.ToString());
                        return;
                    }

                    response.EnsureSuccessStatusCode();
                    var payer = await response.Content.ReadFromJsonAsync<PayerResponse>(WireOptions, cancellationToken)
                        ?? throw new InvalidOperationException("Payer seeding returned an empty response.");
                    LogSeeded(logger, billerId, payer.PayerId, attempt, Activity.Current?.TraceId.ToString());
                    return;
                }
                catch (Exception exception) when (attempt < 3 && exception is not OperationCanceledException)
                {
                    LogSeedRetry(logger, billerId, attempt, Activity.Current?.TraceId.ToString(), exception);
                    await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), cancellationToken);
                }
            }
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            LogSeedError(logger, billerId, activity?.TraceId.ToString(), exception);
            throw;
        }
    }

    private static List<NotificationChannel> MapChannels(IReadOnlyList<string> channels)
    {
        var mapped = new List<NotificationChannel>(channels.Count);
        foreach (var channel in channels)
        {
            if (Enum.TryParse<NotificationChannel>(channel, ignoreCase: true, out var value))
            {
                mapped.Add(value);
            }
        }

        return mapped;
    }

    [LoggerMessage(2600, LogLevel.Information, "Seeded onboarding demo payer {PayerId} for biller {BillerId}, attempt {Attempt}; trace {TraceId}")]
    private static partial void LogSeeded(ILogger logger, string billerId, string payerId, int attempt, string? traceId);
    [LoggerMessage(2601, LogLevel.Warning, "Retrying payer seed for biller {BillerId} after attempt {Attempt}; trace {TraceId}")]
    private static partial void LogSeedRetry(ILogger logger, string billerId, int attempt, string? traceId, Exception exception);
    [LoggerMessage(2602, LogLevel.Information, "Demo payer already seeded for biller {BillerId}; treating as idempotent no-op; trace {TraceId}")]
    private static partial void LogAlreadySeeded(ILogger logger, string billerId, string? traceId);
    [LoggerMessage(2699, LogLevel.Error, "Seeding onboarding demo payer failed for biller {BillerId}; trace {TraceId}")]
    private static partial void LogSeedError(ILogger logger, string billerId, string? traceId, Exception exception);
}
