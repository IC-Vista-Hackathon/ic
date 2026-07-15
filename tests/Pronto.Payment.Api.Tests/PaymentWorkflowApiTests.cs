using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pronto.Invoice.Contracts.V1.Invoices;
using Pronto.Payment.Api.Clients;
using Pronto.Payment.Contracts.V1.Payments;
using Pronto.ServiceDefaults.Errors;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Pronto.Payment.Api.Tests;

/// <summary>
/// End-to-end (in-process) tests for the audited correctness fixes: durable client idempotency,
/// the second-payment-against-scheduled guard, schedule-date validation, payer-account validation,
/// and the no-orphan invoice-transition failure path.
/// </summary>
public sealed class PaymentWorkflowApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private readonly WebApplicationFactory<Program> factory;
    private readonly FakeInvoiceClient fakeInvoices = new();
    private readonly FakePayerAccountValidator fakePayers = new();
    private readonly MutableTimeProvider clock = new(FixedNow);

    public PaymentWorkflowApiTests(WebApplicationFactory<Program> factory)
        => this.factory = factory;

    private HttpClient CreateClient() => factory
        .WithWebHostBuilder(builder =>
        {
            // Deterministic clock and no background settler so tests observe only what they trigger.
            builder.UseSetting("PaymentProcessing:SchedulerEnabled", "false");
            builder.ConfigureServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<IInvoiceClient>(fakeInvoices));
                services.Replace(ServiceDescriptor.Singleton<IPayerAccountValidator>(fakePayers));
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
            });
        })
        .CreateClient();

    private static async Task<PaymentResponse> PostAsync(
        HttpClient client, CreatePaymentRequest request, string? idempotencyKey = null)
    {
        idempotencyKey ??= Guid.NewGuid().ToString();
        using var message = new HttpRequestMessage(HttpMethod.Post, "payments")
        {
            Content = JsonContent.Create(request, options: Wire),
        };
        if (idempotencyKey is not null)
        {
            message.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        var response = await client.SendAsync(message);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PaymentResponse>(Wire))!;
    }

    private static async Task<HttpResponseMessage> PostRawAsync(
        HttpClient client, CreatePaymentRequest request, string? idempotencyKey = null)
    {
        idempotencyKey ??= Guid.NewGuid().ToString();
        using var message = new HttpRequestMessage(HttpMethod.Post, "payments")
        {
            Content = JsonContent.Create(request, options: Wire),
        };
        if (idempotencyKey is not null)
        {
            message.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return await client.SendAsync(message);
    }

    [Fact]
    public async Task PaymentRequiresIdempotencyKey()
    {
        var client = CreateClient();
        var biller = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(biller, 5000);

        var response = await client.PostAsJsonAsync(
            "payments",
            new CreatePaymentRequest(biller, invoice.Id, "card"),
            Wire);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "idempotency_key_required",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task OverlongIdempotencyKeyIsRejected()
    {
        var client = CreateClient();
        var biller = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(biller, 5000);

        var response = await PostRawAsync(
            client,
            new CreatePaymentRequest(biller, invoice.Id, "card"),
            new string('k', 201));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "idempotency_key_too_long",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RetryWithSameIdempotencyKeyReturnsSamePaymentOnce()
    {
        var client = CreateClient();
        var biller = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(biller, 5000);
        var request = new CreatePaymentRequest(biller, invoice.Id, "card");

        var first = await PostAsync(client, request, idempotencyKey: "key-1");
        var second = await PostAsync(client, request, idempotencyKey: "key-1");

        Assert.Equal(first.PaymentId, second.PaymentId);
        Assert.Equal(first.Confirmation, second.Confirmation);
        Assert.Equal(1, fakeInvoices.AppliedTransitions); // no duplicate invoice transition
    }

    [Fact]
    public async Task ReusingIdempotencyKeyForDifferentRequestConflicts()
    {
        var client = CreateClient();
        var biller = Guid.NewGuid().ToString();
        var invoiceA = fakeInvoices.AddDueInvoice(biller, 5000);
        var invoiceB = fakeInvoices.AddDueInvoice(biller, 6000);

        await PostAsync(client, new CreatePaymentRequest(biller, invoiceA.Id, "card"), idempotencyKey: "key-1");
        var conflict = await PostRawAsync(
            client, new CreatePaymentRequest(biller, invoiceB.Id, "card"), idempotencyKey: "key-1");

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Contains("idempotency_key_conflict", await conflict.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SecondPaymentAgainstScheduledInvoiceRejected()
    {
        var client = CreateClient();
        var biller = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(biller, 5000);

        await PostAsync(client, new CreatePaymentRequest(biller, invoice.Id, "ach", ScheduledFor: new DateOnly(2026, 8, 1)));
        var second = await PostRawAsync(client, new CreatePaymentRequest(biller, invoice.Id, "card"));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("invoice_scheduled", await second.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(InvoiceStatus.Scheduled, fakeInvoices.StatusOf(biller, invoice.Id));
    }

    [Fact]
    public async Task PastScheduleDateRejected()
    {
        var client = CreateClient();
        var biller = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(biller, 5000);

        var response = await PostRawAsync(
            client, new CreatePaymentRequest(biller, invoice.Id, "ach", ScheduledFor: new DateOnly(2026, 7, 14)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("invalid_schedule_date", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TodayIsAValidScheduleDate()
    {
        var client = CreateClient();
        var biller = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(biller, 5000);

        var payment = await PostAsync(
            client, new CreatePaymentRequest(biller, invoice.Id, "ach", ScheduledFor: new DateOnly(2026, 7, 15)));

        Assert.Equal(PaymentStatus.Scheduled, payment.Status);
    }

    [Fact]
    public async Task FarFutureScheduleDateRejected()
    {
        var client = CreateClient();
        var biller = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(biller, 5000);

        var response = await PostRawAsync(
            client, new CreatePaymentRequest(biller, invoice.Id, "ach", ScheduledFor: new DateOnly(2099, 1, 1)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("invalid_schedule_date", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownPayerAccountRejected()
    {
        var client = CreateClient();
        var biller = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(biller, 5000);

        var response = await PostRawAsync(
            client, new CreatePaymentRequest(biller, invoice.Id, "card", PayerAccountId: "ghost"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("payer_account_not_found", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(InvoiceStatus.Due, fakeInvoices.StatusOf(biller, invoice.Id)); // rejected before any transition
    }

    [Fact]
    public async Task KnownPayerAccountSucceeds()
    {
        var client = CreateClient();
        var biller = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(biller, 5000);
        fakePayers.Allow("payer-1");

        var payment = await PostAsync(client, new CreatePaymentRequest(biller, invoice.Id, "card", PayerAccountId: "payer-1"));

        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
    }

    [Fact]
    public async Task InvoiceTransitionFailureLeavesNoOrphan()
    {
        var client = CreateClient();
        var biller = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(biller, 5000);
        fakeInvoices.OnUpdateStatus = (_, _, _) =>
            throw ServiceException.Conflict("already_paid", "raced");

        var response = await PostRawAsync(client, new CreatePaymentRequest(biller, invoice.Id, "card"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(InvoiceStatus.Due, fakeInvoices.StatusOf(biller, invoice.Id)); // invoice untouched
        var history = await client.GetFromJsonAsync<PaymentResponse[]>(
            $"payments?biller_id={biller}&invoice_id={invoice.Id}", Wire);
        Assert.DoesNotContain(history!, p => p.Status == PaymentStatus.Succeeded); // no succeeded orphan
    }
}
