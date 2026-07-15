using Pronto.BillerExperience.Api.Infrastructure.Mcp.ServiceClients;
using Pronto.Payment.Contracts.V1.Payments;

namespace Pronto.BillerExperience.Api.Application.Agents;

/// <summary>
/// Pre-fetches server-authoritative quotes for an invoice, one per enabled method, so Financial
/// Planning selects among numbers the Payment Service produced rather than computing fees itself.
/// A method that the Payment Service rejects (e.g. not enabled downstream, or the invoice is already
/// paid) is skipped rather than failing the whole turn — the planner works with whatever quotes
/// resolve, and the caller can log the gap.
/// </summary>
public interface IPaymentQuoteFetcher
{
    ValueTask<IReadOnlyList<PaymentQuoteResponse>> FetchAsync(
        string billerId,
        string invoiceId,
        IReadOnlyList<string> methods,
        CancellationToken cancellationToken);
}

public sealed partial class PaymentQuoteFetcher(
    IPaymentServiceClient payments,
    ILogger<PaymentQuoteFetcher> logger) : IPaymentQuoteFetcher
{
    public async ValueTask<IReadOnlyList<PaymentQuoteResponse>> FetchAsync(
        string billerId,
        string invoiceId,
        IReadOnlyList<string> methods,
        CancellationToken cancellationToken)
    {
        var quotes = new List<PaymentQuoteResponse>(methods.Count);
        foreach (var method in methods)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                quotes.Add(await payments.GetQuoteAsync(billerId, invoiceId, method, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // One method not quoting is expected (disabled downstream, already paid); keep the rest.
                LogQuoteSkipped(logger, billerId, invoiceId, method, exception.Message);
            }
        }

        return quotes;
    }

    [LoggerMessage(3010, LogLevel.Information,
        "Skipped quote for biller {BillerId}, invoice {InvoiceId}, method {Method}: {Reason}")]
    private static partial void LogQuoteSkipped(ILogger logger, string billerId, string invoiceId, string method, string reason);
}
