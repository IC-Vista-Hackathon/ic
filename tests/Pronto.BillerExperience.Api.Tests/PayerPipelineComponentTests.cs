using Microsoft.Extensions.Logging.Abstractions;
using Pronto.BillerExperience.Api.Application.Agents;
using Pronto.BillerExperience.Api.Infrastructure.Mcp.ServiceClients;
using Pronto.Invoice.Contracts.V1.Invoices;
using Pronto.Payment.Contracts.V1.Payments;
using Pronto.PayerAccount.Contracts.V1.Payers;
using Xunit;

namespace Pronto.BillerExperience.Api.Tests;

/// <summary>
/// Bill Intelligence projects an invoice into a bill summary (and 404s a missing one); the quote
/// fetcher asks the Payment Service per method and keeps only the quotes that resolve, so one
/// disabled method doesn't sink the turn.
/// </summary>
public sealed class PayerPipelineComponentTests
{
    [Fact]
    public async Task BillIntelligenceProjectsTheInvoice()
    {
        var invoice = new InvoiceResponse("i_77", "b_1", "acct-1", "Sam Rivers", "Water — July", 8420, new DateOnly(2026, 7, 25), InvoiceStatus.Due);
        var agent = new DeterministicBillIntelligenceAgent(new FakeInvoiceClient(invoice));

        var bill = await agent.SummarizeAsync("b_1", "i_77", CancellationToken.None);

        Assert.Equal("i_77", bill.InvoiceId);
        Assert.Equal(8420, bill.AmountCents);
        Assert.Equal(new DateOnly(2026, 7, 25), bill.DueDate);
        Assert.Equal(InvoiceStatus.Due, bill.Status);
    }

    [Fact]
    public async Task BillIntelligenceThrowsWhenInvoiceMissing()
    {
        var agent = new DeterministicBillIntelligenceAgent(new FakeInvoiceClient(invoice: null));

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            agent.SummarizeAsync("b_1", "i_missing", CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task QuoteFetcherKeepsOnlyMethodsThatQuote()
    {
        // "card" and "ach" quote; "paypal" is not enabled downstream and throws.
        var payments = new FakePaymentClient(quotable: ["card", "ach"]);
        var fetcher = new PaymentQuoteFetcher(payments, NullLogger<PaymentQuoteFetcher>.Instance);

        var quotes = await fetcher.FetchAsync("b_1", "i_77", ["card", "ach", "paypal"], CancellationToken.None);

        Assert.Equal(2, quotes.Count);
        Assert.Contains(quotes, q => q.Method == "card");
        Assert.Contains(quotes, q => q.Method == "ach");
        Assert.DoesNotContain(quotes, q => q.Method == "paypal");
    }

    [Fact]
    public async Task QuoteFetcherReturnsEmptyWhenNoMethodQuotes()
    {
        var payments = new FakePaymentClient(quotable: []);
        var fetcher = new PaymentQuoteFetcher(payments, NullLogger<PaymentQuoteFetcher>.Instance);

        var quotes = await fetcher.FetchAsync("b_1", "i_77", ["card"], CancellationToken.None);

        Assert.Empty(quotes);
    }

    private sealed class FakeInvoiceClient(InvoiceResponse? invoice) : IInvoiceServiceClient
    {
        public ValueTask<InvoiceResponse?> GetAsync(string billerId, string invoiceId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(invoice);

        public ValueTask<InvoiceListResponse> ListAsync(string billerId, string accountNumber, bool includeClosed, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<SeedInvoicesResponse> SeedAsync(string billerId, SeedInvoicesRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakePaymentClient(IReadOnlyCollection<string> quotable) : IPaymentServiceClient
    {
        public ValueTask<PaymentQuoteResponse> GetQuoteAsync(string billerId, string invoiceId, string method, CancellationToken cancellationToken) =>
            quotable.Contains(method)
                ? ValueTask.FromResult(new PaymentQuoteResponse(billerId, invoiceId, method, 8420, 150, 8570))
                : throw new InvalidOperationException($"method '{method}' is not enabled for this biller");

        public ValueTask<IReadOnlyList<PaymentResponse>> ListAsync(string billerId, string payerAccountId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<PaymentResponse> CreateAsync(CreatePaymentRequest request, string idempotencyKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
