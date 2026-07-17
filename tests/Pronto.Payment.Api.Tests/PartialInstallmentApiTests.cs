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

/// <summary>
/// Server-authoritative partial payments and installment plans (feature F4). Every requested
/// amount and plan is validated against the balance the server looks up and the biller's policy —
/// never a client field — and the full-payment path stays the default.
/// </summary>
public sealed class PartialInstallmentApiTests : IClassFixture<TestingAppFactory>
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private readonly FakeInvoiceClient fakeInvoices = new();
    private readonly FakeBillerConfigClient fakeConfig = new();
    private readonly HttpClient client;

    public PartialInstallmentApiTests(TestingAppFactory factory)
    {
        client = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<IInvoiceClient>(fakeInvoices));
                services.Replace(ServiceDescriptor.Singleton<IBillerConfigClient>(fakeConfig));
            }))
            .CreateClient();
    }

    private Task<HttpResponseMessage> PostAsync(CreatePaymentRequest request)
        => client.PostAsJsonAsync("payments", request, Wire);

    private async Task<int> OutstandingAsync(string billerId, string invoiceId)
    {
        var quote = await client.GetFromJsonAsync<PaymentQuoteResponse>(
            $"payments/quote?biller_id={billerId}&invoice_id={invoiceId}&method=card", Wire);
        return quote!.OutstandingCents;
    }

    [Fact]
    public async Task FullPaymentPathIsUnchangedWhenNoAmountRequested()
    {
        var billerId = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(billerId, amountCents: 8420);

        var response = await PostAsync(
            new CreatePaymentRequest(billerId, invoice.Id, "card", IdempotencyKey: "full"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payment = await response.Content.ReadFromJsonAsync<PaymentResponse>(Wire);
        Assert.Equal(8420, payment!.AmountCents);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Null(payment.InstallmentPlanId);
        Assert.Equal(Pronto.Invoice.Contracts.V1.Invoices.InvoiceStatus.Paid, fakeInvoices.StatusOf(billerId, invoice.Id));
    }

    [Fact]
    public async Task OverBalancePartialRejected()
    {
        var billerId = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(billerId, amountCents: 5000);

        var response = await PostAsync(
            new CreatePaymentRequest(billerId, invoice.Id, "card", IdempotencyKey: "over", AmountCents: 5001));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("amount_exceeds_balance", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BelowMinimumPartialRejected()
    {
        var billerId = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(billerId, amountCents: 5000);

        var response = await PostAsync(
            new CreatePaymentRequest(billerId, invoice.Id, "card", IdempotencyKey: "low", AmountCents: 500));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("amount_below_minimum", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PartialPaymentsDisabledRejectsPartialButAllowsFull()
    {
        fakeConfig.Config = fakeConfig.Config with { PartialPaymentsAllowed = false };
        var billerId = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(billerId, amountCents: 5000);

        var partial = await PostAsync(
            new CreatePaymentRequest(billerId, invoice.Id, "card", IdempotencyKey: "p", AmountCents: 2000));
        Assert.Equal(HttpStatusCode.BadRequest, partial.StatusCode);
        Assert.Contains("partial_payments_not_allowed", await partial.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // Paying the exact full balance is always allowed, even with partials disabled.
        var full = await PostAsync(
            new CreatePaymentRequest(billerId, invoice.Id, "card", IdempotencyKey: "f", AmountCents: 5000));
        Assert.Equal(HttpStatusCode.Created, full.StatusCode);
    }

    [Fact]
    public async Task ValidPartialReducesBalanceRepeatsIdempotentlyAndFeeIsOnPaidAmount()
    {
        var billerId = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(billerId, amountCents: 10000);

        var request = new CreatePaymentRequest(billerId, invoice.Id, "card", IdempotencyKey: "part-1", AmountCents: 4000);
        var first = await PostAsync(request);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstPayment = await first.Content.ReadFromJsonAsync<PaymentResponse>(Wire);
        Assert.Equal(4000, firstPayment!.AmountCents);
        Assert.Equal(100, firstPayment.FeeCents); // 2.5% of 4000, not of 10000
        Assert.Equal(PaymentStatus.Succeeded, firstPayment.Status);

        // Invoice not settled: the partial left a balance, so it stays due.
        Assert.Equal(Pronto.Invoice.Contracts.V1.Invoices.InvoiceStatus.Due, fakeInvoices.StatusOf(billerId, invoice.Id));
        Assert.Equal(6000, await OutstandingAsync(billerId, invoice.Id));

        // Idempotent replay: same key returns the same payment (200), balance unchanged.
        var replay = await PostAsync(request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayed = await replay.Content.ReadFromJsonAsync<PaymentResponse>(Wire);
        Assert.Equal(firstPayment.PaymentId, replayed!.PaymentId);
        Assert.Equal(6000, await OutstandingAsync(billerId, invoice.Id));

        // A second partial that clears the remaining balance settles the invoice.
        var second = await PostAsync(
            new CreatePaymentRequest(billerId, invoice.Id, "card", IdempotencyKey: "part-2", AmountCents: 6000));
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(Pronto.Invoice.Contracts.V1.Invoices.InvoiceStatus.Paid, fakeInvoices.StatusOf(billerId, invoice.Id));
    }

    [Fact]
    public async Task ReusingKeyWithDifferentAmountConflicts()
    {
        var billerId = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(billerId, amountCents: 10000);

        await PostAsync(new CreatePaymentRequest(billerId, invoice.Id, "card", IdempotencyKey: "k", AmountCents: 4000));
        var reused = await PostAsync(new CreatePaymentRequest(billerId, invoice.Id, "card", IdempotencyKey: "k", AmountCents: 5000));

        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        Assert.Contains("idempotency_key_conflict", await reused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallmentEnrollmentCreatesExpectedSchedule()
    {
        var billerId = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(billerId, amountCents: 10000);

        var response = await PostAsync(
            new CreatePaymentRequest(billerId, invoice.Id, "card", IdempotencyKey: "plan", InstallmentCount: 3));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var plan = await response.Content.ReadFromJsonAsync<InstallmentPlanResponse>(Wire);
        Assert.NotNull(plan);
        Assert.Equal(3, plan.InstallmentCount);
        Assert.Equal(10000, plan.TotalAmountCents);
        Assert.Equal(3, plan.Installments.Count);
        // Remainder cents land on the earliest installments; the schedule sums to the balance.
        int[] expectedAmounts = [3334, 3333, 3333];
        int[] expectedSequences = [0, 1, 2];
        Assert.Equal(expectedAmounts, plan.Installments.Select(i => i.AmountCents).ToArray());
        Assert.Equal(expectedSequences, plan.Installments.Select(i => i.InstallmentSequence!.Value).ToArray());
        Assert.All(plan.Installments, i =>
        {
            Assert.Equal(PaymentStatus.Scheduled, i.Status);
            Assert.NotNull(i.ScheduledFor);
            Assert.Equal(plan.InstallmentPlanId, i.InstallmentPlanId);
        });

        // Enrollment reserves no invoice state; it stays due while the plan is outstanding.
        Assert.Equal(Pronto.Invoice.Contracts.V1.Invoices.InvoiceStatus.Due, fakeInvoices.StatusOf(billerId, invoice.Id));

        // Idempotent replay returns the same plan.
        var replay = await PostAsync(
            new CreatePaymentRequest(billerId, invoice.Id, "card", IdempotencyKey: "plan", InstallmentCount: 3));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayed = await replay.Content.ReadFromJsonAsync<InstallmentPlanResponse>(Wire);
        Assert.Equal(plan.InstallmentPlanId, replayed!.InstallmentPlanId);
    }

    [Fact]
    public async Task InstallmentsDisabledRejected()
    {
        fakeConfig.Config = fakeConfig.Config with { InstallmentsAllowed = false };
        var billerId = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(billerId, amountCents: 10000);

        var response = await PostAsync(
            new CreatePaymentRequest(billerId, invoice.Id, "card", IdempotencyKey: "plan", InstallmentCount: 3));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("installments_not_allowed", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallmentCountAboveMaximumRejected()
    {
        fakeConfig.Config = fakeConfig.Config with { MaxInstallments = 4 };
        var billerId = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(billerId, amountCents: 10000);

        var response = await PostAsync(
            new CreatePaymentRequest(billerId, invoice.Id, "card", IdempotencyKey: "plan", InstallmentCount: 5));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("installment_count_exceeds_max", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SingleInstallmentRejected()
    {
        var billerId = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(billerId, amountCents: 10000);

        var response = await PostAsync(
            new CreatePaymentRequest(billerId, invoice.Id, "card", IdempotencyKey: "plan", InstallmentCount: 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("invalid_installment_count", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
