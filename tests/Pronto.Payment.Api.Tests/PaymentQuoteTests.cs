using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pronto.Payment.Api.Clients;
using Pronto.Payment.Contracts.V1.Payments;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Pronto.Payment.Api.Tests;

/// <summary>The quote a payer approves must equal what the payment then charges.</summary>
public sealed class PaymentQuoteTests : IClassFixture<TestingAppFactory>
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private readonly FakeInvoiceClient fakeInvoices = new();
    private readonly HttpClient client;

    public PaymentQuoteTests(TestingAppFactory factory)
    {
        client = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.Replace(ServiceDescriptor.Singleton<IInvoiceClient>(fakeInvoices))))
            .CreateClient();
    }

    [Fact]
    public async Task QuoteMatchesSubsequentPaymentExactly()
    {
        var billerId = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(billerId, amountCents: 8420);

        var quote = await client.GetFromJsonAsync<PaymentQuoteResponse>(
            $"payments/quote?biller_id={billerId}&invoice_id={invoice.Id}&method=card", Wire);
        var payment = await (await client.PostAsJsonAsync(
            "payments",
            new CreatePaymentRequest(billerId, invoice.Id, "card", IdempotencyKey: "quote-payment"),
            Wire))
            .Content.ReadFromJsonAsync<PaymentResponse>(Wire);

        Assert.NotNull(quote);
        Assert.NotNull(payment);
        Assert.Equal(quote.FeeCents, payment.FeeCents);
        Assert.Equal(quote.TotalCents, payment.TotalCents);
        Assert.Equal(quote.AmountCents, payment.AmountCents);
    }

    [Fact]
    public async Task QuoteUsesFlatFeeForAch()
    {
        var billerId = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(billerId, amountCents: 10000);

        var quote = await client.GetFromJsonAsync<PaymentQuoteResponse>(
            $"payments/quote?biller_id={billerId}&invoice_id={invoice.Id}&method=ach", Wire);

        Assert.Equal(150, quote!.FeeCents);
        Assert.Equal(10150, quote.TotalCents);
    }

    [Fact]
    public async Task QuoteForPaidInvoiceConflicts()
    {
        var billerId = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(billerId, amountCents: 5000);
        await client.PostAsJsonAsync(
            "payments",
            new CreatePaymentRequest(billerId, invoice.Id, "card", IdempotencyKey: "paid-invoice"),
            Wire);

        var response = await client.GetAsync(
            new Uri($"payments/quote?biller_id={billerId}&invoice_id={invoice.Id}&method=card", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task QuoteRejectsUnknownMethodAndMissingInvoice()
    {
        var billerId = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(billerId, amountCents: 5000);

        var badMethod = await client.GetAsync(
            new Uri($"payments/quote?biller_id={billerId}&invoice_id={invoice.Id}&method=crypto", UriKind.Relative));
        var missing = await client.GetAsync(
            new Uri($"payments/quote?biller_id={billerId}&invoice_id={Guid.NewGuid()}&method=card", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, badMethod.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task QuoteRequiresAllQueryParameters()
    {
        var billerId = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(billerId, amountCents: 5000);
        var cases = new[]
        {
            ($"invoice_id={invoice.Id}&method=card", "biller_id_required"),
            ($"biller_id={billerId}&method=card", "invoice_id_required"),
            ($"biller_id={billerId}&invoice_id={invoice.Id}", "method_required"),
        };

        foreach (var (query, errorCode) in cases)
        {
            var response = await client.GetAsync(new Uri($"payments/quote?{query}", UriKind.Relative));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(errorCode, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
    }
}
