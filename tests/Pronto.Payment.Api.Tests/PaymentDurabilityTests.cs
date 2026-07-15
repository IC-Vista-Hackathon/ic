using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pronto.Invoice.Contracts.V1.Invoices;
using Pronto.Payment.Api.Clients;
using Pronto.Payment.Api.Storage;
using Pronto.Payment.Contracts.V1.Payments;
using Pronto.ServiceDefaults.Errors;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Pronto.Payment.Api.Tests;

/// <summary>
/// Durability + idempotency contract for <c>POST /payments</c>: the payment row is persisted
/// (Pending) before the invoice transition, and an <c>Idempotency-Key</c> makes a retry return
/// the original result instead of double-charging or 409ing.
/// </summary>
public sealed class PaymentDurabilityTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private readonly FakeInvoiceClient fakeInvoices = new();
    private readonly InMemoryPaymentStore store = new();
    private readonly HttpClient client;

    public PaymentDurabilityTests(WebApplicationFactory<Program> factory)
    {
        client = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<IInvoiceClient>(fakeInvoices));
                services.Replace(ServiceDescriptor.Singleton<IPaymentStore>(store));
            }))
            .CreateClient();
    }

    [Fact]
    public async Task PaymentRowPersistsAsPendingBeforeInvoiceTransition()
    {
        var billerId = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(billerId, amountCents: 5000);
        // Simulate a crash/contention at the invoice transition — the step AFTER the persist.
        fakeInvoices.UpdateStatusFault = ServiceException.Conflict("already_paid", "lost the race");

        var response = await client.PostAsJsonAsync(
            "payments", new CreatePaymentRequest(billerId, invoice.Id, "card"), Wire);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // Persist-before-mark: a recoverable Pending payment row exists even though the mark failed.
        var persisted = await store.ListAsync(billerId, null, invoice.Id, CancellationToken.None);
        var pending = Assert.Single(persisted);
        Assert.Equal(PaymentStatus.Pending, pending.Status);
        Assert.Equal(invoice.Id, pending.InvoiceId);
    }

    [Fact]
    public async Task SuccessfulPaymentEndsSucceededWithSingleRow()
    {
        var billerId = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(billerId, amountCents: 5000);

        var response = await client.PostAsJsonAsync(
            "payments", new CreatePaymentRequest(billerId, invoice.Id, "card"), Wire);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var stored = await store.ListAsync(billerId, null, invoice.Id, CancellationToken.None);
        var payment = Assert.Single(stored);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
    }

    [Fact]
    public async Task RepeatWithSameIdempotencyKeyReturnsOriginal()
    {
        var billerId = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(billerId, amountCents: 8420);
        var key = Guid.NewGuid().ToString();

        var first = await PostWithKeyAsync(billerId, invoice.Id, key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstPayment = await first.Content.ReadFromJsonAsync<PaymentResponse>(Wire);

        var second = await PostWithKeyAsync(billerId, invoice.Id, key);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondPayment = await second.Content.ReadFromJsonAsync<PaymentResponse>(Wire);
        Assert.Equal(firstPayment!.PaymentId, secondPayment!.PaymentId);
        Assert.Equal(firstPayment.Confirmation, secondPayment.Confirmation);
        // The invoice was only transitioned once — no double charge on replay.
        Assert.Equal(1, fakeInvoices.UpdateStatusCalls);
        Assert.Single(await store.ListAsync(billerId, null, invoice.Id, CancellationToken.None));
    }

    [Fact]
    public async Task RetryAfterMidFlightCrashResumesPendingPaymentToSucceeded()
    {
        var billerId = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(billerId, amountCents: 5000);
        var key = Guid.NewGuid().ToString();
        // First attempt crashes at the invoice transition, leaving a reserved key + Pending row.
        fakeInvoices.UpdateStatusFault = new InvalidOperationException("process crashed mid-flight");

        var first = await PostWithKeyAsync(billerId, invoice.Id, key);
        Assert.Equal(HttpStatusCode.InternalServerError, first.StatusCode);
        var stuck = Assert.Single(await store.ListAsync(billerId, null, invoice.Id, CancellationToken.None));
        Assert.Equal(PaymentStatus.Pending, stuck.Status);

        // Retry with the same key drives the stuck payment to completion instead of returning Pending.
        var second = await PostWithKeyAsync(billerId, invoice.Id, key);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var resumed = await second.Content.ReadFromJsonAsync<PaymentResponse>(Wire);
        Assert.Equal(stuck.PaymentId, resumed!.PaymentId);
        Assert.Equal(PaymentStatus.Succeeded, resumed.Status);
        Assert.Equal(InvoiceStatus.Paid, fakeInvoices.StatusOf(billerId, invoice.Id));
        Assert.Single(await store.ListAsync(billerId, null, invoice.Id, CancellationToken.None));
    }

    [Fact]
    public async Task OverlongIdempotencyKeyIsRejected()
    {
        var billerId = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(billerId, amountCents: 5000);

        var response = await PostWithKeyAsync(billerId, invoice.Id, new string('k', 201));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await store.ListAsync(billerId, null, invoice.Id, CancellationToken.None));
    }

    [Fact]
    public async Task RepeatWithoutIdempotencyKeyStillConflicts()
    {
        var billerId = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(billerId, amountCents: 5000);
        await client.PostAsJsonAsync(
            "payments", new CreatePaymentRequest(billerId, invoice.Id, "card"), Wire);

        var second = await client.PostAsJsonAsync(
            "payments", new CreatePaymentRequest(billerId, invoice.Id, "card"), Wire);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    private async Task<HttpResponseMessage> PostWithKeyAsync(string billerId, string invoiceId, string key)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "payments")
        {
            Content = JsonContent.Create(new CreatePaymentRequest(billerId, invoiceId, "card"), options: Wire),
        };
        message.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(message);
    }
}
