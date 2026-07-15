using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pronto.Payment.Api.Clients;
using Pronto.Payment.Contracts.V1.Payments;
using Pronto.Payment.Contracts.V1.Purchases;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Pronto.Payment.Api.Tests;

public sealed class PaymentsApiTests : IClassFixture<TestingAppFactory>
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private readonly FakeInvoiceClient fakeInvoices = new();
    private readonly HttpClient client;

    public PaymentsApiTests(TestingAppFactory factory)
    {
        client = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.Replace(ServiceDescriptor.Singleton<IInvoiceClient>(fakeInvoices))))
            .CreateClient();
    }

    [Fact]
    public async Task CardPaymentSucceedsWithPercentFee()
    {
        var billerId = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(billerId, amountCents: 8420);

        var response = await client.PostAsJsonAsync(
            "payments", new CreatePaymentRequest(billerId, invoice.Id, "card"), Wire);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payment = await response.Content.ReadFromJsonAsync<PaymentResponse>(Wire);
        Assert.NotNull(payment);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal(8420, payment.AmountCents);
        Assert.Equal(211, payment.FeeCents); // 2.5% of 8420 = 210.5 → away-from-zero → 211
        Assert.Equal(8631, payment.TotalCents);
        Assert.StartsWith("PRONTO-", payment.Confirmation, StringComparison.Ordinal);

        var fetched = await client.GetFromJsonAsync<PaymentResponse>(
            $"payments/{payment.PaymentId}?biller_id={billerId}", Wire);
        Assert.Equal(payment.PaymentId, fetched!.PaymentId);
    }

    [Fact]
    public async Task AchPaymentUsesFlatFee()
    {
        var billerId = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(billerId, amountCents: 10000);

        var response = await client.PostAsJsonAsync(
            "payments", new CreatePaymentRequest(billerId, invoice.Id, "ach"), Wire);

        var payment = await response.Content.ReadFromJsonAsync<PaymentResponse>(Wire);
        Assert.Equal(150, payment!.FeeCents);
        Assert.Equal(10150, payment.TotalCents);
    }

    [Fact]
    public async Task ScheduledPaymentReturnsScheduledStatus()
    {
        var billerId = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(billerId, amountCents: 5000);

        var response = await client.PostAsJsonAsync(
            "payments",
            new CreatePaymentRequest(
                billerId, invoice.Id, "ach", ScheduledFor: new DateOnly(2026, 7, 24)),
            Wire);

        var payment = await response.Content.ReadFromJsonAsync<PaymentResponse>(Wire);
        Assert.Equal(PaymentStatus.Scheduled, payment!.Status);
        Assert.Equal(new DateOnly(2026, 7, 24), payment.ScheduledFor);
    }

    [Fact]
    public async Task PayingPaidInvoiceConflicts()
    {
        var billerId = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(billerId, amountCents: 5000);
        await client.PostAsJsonAsync(
            "payments", new CreatePaymentRequest(billerId, invoice.Id, "card"), Wire);

        var second = await client.PostAsJsonAsync(
            "payments", new CreatePaymentRequest(billerId, invoice.Id, "card"), Wire);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains(
            "already_paid", await second.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownMethodRejected()
    {
        var billerId = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(billerId, amountCents: 5000);

        var response = await client.PostAsJsonAsync(
            "payments", new CreatePaymentRequest(billerId, invoice.Id, "crypto"), Wire);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "method_not_enabled", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PurchaseSucceedsOnceThenConflicts()
    {
        var billerId = Guid.NewGuid().ToString();

        var first = await client.PostAsJsonAsync(
            "purchases", new CreatePurchaseRequest(billerId, PurchasePlan.Isolated), Wire);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var purchase = await first.Content.ReadFromJsonAsync<PurchaseResponse>(Wire);
        Assert.Equal(199900, purchase!.AmountCents);
        Assert.Equal(PurchaseStatus.Paid, purchase.Status);

        var second = await client.PostAsJsonAsync(
            "purchases", new CreatePurchaseRequest(billerId, PurchasePlan.Shared), Wire);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task PaymentHistoryCanBeFilteredByPayerAccount()
    {
        var billerId = Guid.NewGuid().ToString();
        var firstInvoice = fakeInvoices.AddDueInvoice(billerId, amountCents: 4200);
        var secondInvoice = fakeInvoices.AddDueInvoice(billerId, amountCents: 5300);
        await client.PostAsJsonAsync("payments", new CreatePaymentRequest(billerId, firstInvoice.Id, "card", "payer-1"), Wire);
        await client.PostAsJsonAsync("payments", new CreatePaymentRequest(billerId, secondInvoice.Id, "ach", "payer-2"), Wire);

        var history = await client.GetFromJsonAsync<PaymentResponse[]>(
            $"payments?biller_id={billerId}&payer_account_id=payer-1", Wire);

        var payment = Assert.Single(history!);
        Assert.Equal("payer-1", payment.PayerAccountId);
        Assert.Equal(firstInvoice.Id, payment.InvoiceId);
    }
}
