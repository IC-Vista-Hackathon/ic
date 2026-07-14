using System.Diagnostics;
using System.Net.Http.Json;

namespace Pronto.BillerExperience.Api.Infrastructure.SupportingServices;

public sealed partial class HttpInvoiceSeeder(HttpClient http, ILogger<HttpInvoiceSeeder> logger) : IInvoiceSeeder
{
    public async ValueTask SeedAsync(string billerId, string billType, CancellationToken cancellationToken)
    {
        using var activity = BillerExperienceTelemetry.Source.StartActivity("invoice.seed");
        activity?.SetTag("ic.biller_id", billerId);
        try
        {
            using var response = await http.PostAsJsonAsync($"billers/{Uri.EscapeDataString(billerId)}/invoices/seed",
                new { count = 3, account_number = "4421", bill_type = billType }, cancellationToken);
            response.EnsureSuccessStatusCode();
            LogSeeded(logger, billerId);
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            LogSeedError(logger, billerId, activity?.TraceId.ToString(), exception);
            throw;
        }
    }

    [LoggerMessage(2500, LogLevel.Information, "Seeded onboarding invoices for biller {BillerId}")]
    private static partial void LogSeeded(ILogger logger, string billerId);
    [LoggerMessage(2599, LogLevel.Error, "Seeding onboarding invoices failed for biller {BillerId}; trace {TraceId}")]
    private static partial void LogSeedError(ILogger logger, string billerId, string? traceId, Exception exception);
}
