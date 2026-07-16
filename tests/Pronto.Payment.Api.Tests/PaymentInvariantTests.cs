using System.Net;
using System.Net.Http.Json;
using System.Text;
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
/// The Payment service is the authoritative gate for "a payment is a real payment". These tests
/// prove each server-side invariant, including the negative cases: the charged amount always comes
/// from the invoice (never the client), fee/total come from <c>FeeCalculator</c> and match the
/// quote, idempotency is required and fingerprint-checked, invoice payability holds, the method
/// must be enabled, and the biller's configuration must have cleared the publish + compliance gate.
/// </summary>
public sealed class PaymentInvariantTests
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private readonly FakeInvoiceClient fakeInvoices = new();
    private readonly FakeBillerConfigClient fakeConfigs = new();
    private readonly HttpClient client;

    public PaymentInvariantTests()
    {
        client = new TestingAppFactory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<IInvoiceClient>(fakeInvoices));
                services.Replace(ServiceDescriptor.Singleton<IBillerConfigClient>(fakeConfigs));
            }))
            .CreateClient();
    }

    [Fact]
    public async Task ChargedAmountAlwaysComesFromInvoiceNotClient()
    {
        var billerId = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(billerId, amountCents: 7300);

        // The wire contract has no amount/fee/total field, and unknown members are rejected — so a
        // client attempting to dictate the charge cannot even reach the handler.
        using var tampered = new StringContent(
            $$"""{"biller_id":"{{billerId}}","invoice_id":"{{invoice.Id}}","method":"card","amount_cents":1,"fee_cents":0,"total_cents":1,"idempotency_key":"tampered"}""",
            Encoding.UTF8,
            "application/json");
        var rejected = await client.PostAsync(new Uri("payments", UriKind.Relative), tampered);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        // A well-formed request charges the invoice's amount regardless of anything the client sends.
        var response = await client.PostAsJsonAsync(
            "payments",
            new CreatePaymentRequest(billerId, invoice.Id, "card", IdempotencyKey: "honest"),
            Wire);
        var payment = await response.Content.ReadFromJsonAsync<PaymentResponse>(Wire);
        Assert.Equal(7300, payment!.AmountCents);
        Assert.Equal(183, payment.FeeCents); // 2.5% of 7300 = 182.5 → away-from-zero → 183
        Assert.Equal(7483, payment.TotalCents);
    }

    [Fact]
    public async Task QuoteTotalEqualsChargedTotal()
    {
        var billerId = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(billerId, amountCents: 9990);

        var quote = await client.GetFromJsonAsync<PaymentQuoteResponse>(
            $"payments/quote?biller_id={billerId}&invoice_id={invoice.Id}&method=card", Wire);
        var payment = await (await client.PostAsJsonAsync(
            "payments",
            new CreatePaymentRequest(billerId, invoice.Id, "card", IdempotencyKey: "quote-match"),
            Wire)).Content.ReadFromJsonAsync<PaymentResponse>(Wire);

        Assert.Equal(quote!.AmountCents, payment!.AmountCents);
        Assert.Equal(quote.FeeCents, payment.FeeCents);
        Assert.Equal(quote.TotalCents, payment.TotalCents);
    }

    [Fact]
    public async Task IdempotencyKeyIsRequired()
    {
        var billerId = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(billerId, amountCents: 5000);

        var response = await client.PostAsJsonAsync(
            "payments", new CreatePaymentRequest(billerId, invoice.Id, "card"), Wire);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "idempotency_key_required", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DuplicateIdempotencyKeyReplaysSameOutcomeExactlyOnce()
    {
        var billerId = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(billerId, amountCents: 6400);
        var request = new CreatePaymentRequest(billerId, invoice.Id, "card", IdempotencyKey: "dup-key");

        var first = await client.PostAsJsonAsync("payments", request, Wire);
        var second = await client.PostAsJsonAsync("payments", request, Wire);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode); // replay, not a new resource
        var firstPayment = await first.Content.ReadFromJsonAsync<PaymentResponse>(Wire);
        var secondPayment = await second.Content.ReadFromJsonAsync<PaymentResponse>(Wire);
        Assert.Equal(firstPayment!.PaymentId, secondPayment!.PaymentId);
        Assert.Equal(firstPayment.TotalCents, secondPayment.TotalCents);

        // Exactly one record persisted for the invoice despite the retry.
        var history = await client.GetFromJsonAsync<PaymentResponse[]>(
            $"payments?biller_id={billerId}&invoice_id={invoice.Id}", Wire);
        var only = Assert.Single(history!);
        Assert.Equal(firstPayment.PaymentId, only.PaymentId);
    }

    [Fact]
    public async Task SameKeyDifferentFingerprintConflicts()
    {
        var billerId = Guid.NewGuid().ToString();
        var firstInvoice = fakeInvoices.AddDueInvoice(billerId, amountCents: 5000);
        var secondInvoice = fakeInvoices.AddDueInvoice(billerId, amountCents: 5000);

        await client.PostAsJsonAsync(
            "payments",
            new CreatePaymentRequest(billerId, firstInvoice.Id, "card", IdempotencyKey: "shared-key"),
            Wire);
        var conflict = await client.PostAsJsonAsync(
            "payments",
            new CreatePaymentRequest(billerId, secondInvoice.Id, "card", IdempotencyKey: "shared-key"),
            Wire);

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Contains(
            "idempotency_key_conflict", await conflict.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PayingScheduledInvoiceConflicts()
    {
        var billerId = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(billerId, amountCents: 5000);
        var scheduledFor = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3);

        var scheduled = await client.PostAsJsonAsync(
            "payments",
            new CreatePaymentRequest(
                billerId, invoice.Id, "ach", ScheduledFor: scheduledFor, IdempotencyKey: "sched"),
            Wire);
        Assert.Equal(HttpStatusCode.Created, scheduled.StatusCode);

        var second = await client.PostAsJsonAsync(
            "payments",
            new CreatePaymentRequest(billerId, invoice.Id, "card", IdempotencyKey: "sched-second"),
            Wire);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains(
            "invoice_scheduled", await second.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MethodNotEnabledIsRejected()
    {
        var billerId = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(billerId, amountCents: 5000);
        fakeConfigs.PaymentMethods = ["ach"]; // card disabled for this biller

        var response = await client.PostAsJsonAsync(
            "payments",
            new CreatePaymentRequest(billerId, invoice.Id, "card", IdempotencyKey: "no-card"),
            Wire);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "method_not_enabled", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(BillerSettlementState.Unpublished)]
    [InlineData(BillerSettlementState.ComplianceNotPassed)]
    public async Task UnpublishedOrNonCompliantBillerIsRejected(BillerSettlementState state)
    {
        var billerId = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(billerId, amountCents: 5000);
        fakeConfigs.SetSettlementState(billerId, state);

        var response = await client.PostAsJsonAsync(
            "payments",
            new CreatePaymentRequest(billerId, invoice.Id, "card", IdempotencyKey: "not-live"),
            Wire);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(
            "biller_not_publishable", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // No payment record should have been created for the rejected biller.
        var history = await client.GetFromJsonAsync<PaymentResponse[]>(
            $"payments?biller_id={billerId}", Wire);
        Assert.Empty(history!);
    }

    [Fact]
    public async Task RetryReplaysOriginalOutcomeEvenAfterBillerBecomesIneligible()
    {
        var billerId = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(billerId, amountCents: 5000);
        var request = new CreatePaymentRequest(billerId, invoice.Id, "card", IdempotencyKey: "recover-me");

        var first = await client.PostAsJsonAsync("payments", request, Wire);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var original = await first.Content.ReadFromJsonAsync<PaymentResponse>(Wire);

        // The biller loses its settle-eligibility after the payment already succeeded; a retried
        // request (lost confirmation) must still replay the original outcome, not be refused.
        fakeConfigs.SetSettlementState(billerId, BillerSettlementState.Unpublished);

        var replay = await client.PostAsJsonAsync("payments", request, Wire);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayed = await replay.Content.ReadFromJsonAsync<PaymentResponse>(Wire);
        Assert.Equal(original!.PaymentId, replayed!.PaymentId);
        Assert.Equal(PaymentStatus.Succeeded, replayed.Status);

        // A genuinely new payment for the now-ineligible biller is still blocked.
        var otherInvoice = fakeInvoices.AddDueInvoice(billerId, amountCents: 5000);
        var blocked = await client.PostAsJsonAsync(
            "payments",
            new CreatePaymentRequest(billerId, otherInvoice.Id, "card", IdempotencyKey: "new-one"),
            Wire);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Contains(
            "biller_not_publishable", await blocked.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishedBillerCanSettle()
    {
        var billerId = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(billerId, amountCents: 5000);
        fakeConfigs.SetSettlementState(billerId, BillerSettlementState.Published);

        var response = await client.PostAsJsonAsync(
            "payments",
            new CreatePaymentRequest(billerId, invoice.Id, "card", IdempotencyKey: "live"),
            Wire);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
