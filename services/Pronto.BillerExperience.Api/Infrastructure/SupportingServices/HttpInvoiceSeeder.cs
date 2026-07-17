using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pronto.Invoice.Contracts.V1.Invoices;

namespace Pronto.BillerExperience.Api.Infrastructure.SupportingServices;

public sealed partial class HttpInvoiceSeeder(
    HttpClient http,
    ISeedInvoiceGenerator generator,
    ILogger<HttpInvoiceSeeder> logger) : IInvoiceSeeder
{
    // Demo preview account every seeded biller's invoices attach to (matches the payer preview).
    private const string PreviewAccountNumber = "4421";
    private const int SeedCount = 4;

    private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) },
    };

    public async ValueTask SeedAsync(SeedBillerContext biller, CancellationToken cancellationToken, bool replace = false)
    {
        ArgumentNullException.ThrowIfNull(biller);
        var billerId = biller.BillerId;
        // Agent configures: choose biller-relevant demo line items here; the Invoice service persists.
        var invoiceSpecs = generator.Generate(biller, SeedCount);
        // Capture the correlation id from the ambient request before the child activity becomes
        // current, then carry it onto this span so CorrelationPropagationHandler propagates the
        // x-correlation-id/x-ic-biller-id headers on the outbound seed request.
        var correlationId = Activity.Current?.GetTagItem("ic.correlation_id")?.ToString();
        using var activity = BillerExperienceTelemetry.Source.StartActivity("invoice.seed");
        activity?.SetTag("ic.biller_id", billerId);
        if (!string.IsNullOrWhiteSpace(correlationId)) activity?.SetTag("ic.correlation_id", correlationId);
        try
        {
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post,
                        $"billers/{Uri.EscapeDataString(billerId)}/invoices/seed")
                    {
                        Content = JsonContent.Create(
                            new SeedInvoicesRequest(invoiceSpecs.Count, PreviewAccountNumber, biller.BillType, invoiceSpecs, replace),
                            options: WireOptions)
                    };
                    using var response = await http.SendAsync(request, cancellationToken);
                    response.EnsureSuccessStatusCode();
                    var result = await response.Content.ReadFromJsonAsync<SeedInvoicesResponse>(WireOptions, cancellationToken)
                        ?? throw new InvalidOperationException("Invoice seeding returned an empty response.");
                    if (result.Seeded < 1) throw new InvalidOperationException("Invoice seeding returned no invoices.");
                    LogSeeded(logger, billerId, result.AccountNumber, result.Seeded, attempt, Activity.Current?.TraceId.ToString());
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

    [LoggerMessage(2500, LogLevel.Information, "Seeded {InvoiceCount} onboarding invoices for biller {BillerId}, account {AccountNumber}, attempt {Attempt}; trace {TraceId}")]
    private static partial void LogSeeded(ILogger logger, string billerId, string accountNumber, int invoiceCount, int attempt, string? traceId);
    [LoggerMessage(2501, LogLevel.Warning, "Retrying invoice seed for biller {BillerId} after attempt {Attempt}; trace {TraceId}")]
    private static partial void LogSeedRetry(ILogger logger, string billerId, int attempt, string? traceId, Exception exception);
    [LoggerMessage(2599, LogLevel.Error, "Seeding onboarding invoices failed for biller {BillerId}; trace {TraceId}")]
    private static partial void LogSeedError(ILogger logger, string billerId, string? traceId, Exception exception);
}
