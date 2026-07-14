using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using IC.Invoice.Contracts.V1.Invoices;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace IC.Invoice.Api.Tests;

public sealed class InvoicesApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient client;

    public InvoicesApiTests(WebApplicationFactory<Program> factory)
    {
        client = factory.CreateClient();
    }

    [Fact]
    public async Task SeedThenListReturnsOpenInvoices()
    {
        var billerId = Guid.NewGuid().ToString();

        var seedResponse = await client.PostAsJsonAsync(
            $"billers/{billerId}/invoices/seed",
            new SeedInvoicesRequest(Count: 3, AccountNumber: "ACCT-1"),
            Wire);
        Assert.Equal(HttpStatusCode.Created, seedResponse.StatusCode);

        var list = await client.GetFromJsonAsync<InvoiceListResponse>(
            $"billers/{billerId}/invoices?accountNumber=ACCT-1", Wire);

        Assert.NotNull(list);
        Assert.Equal(3, list.Invoices.Count);
        Assert.All(list.Invoices, invoice => Assert.Equal(InvoiceStatus.Due, invoice.Status));
    }

    [Fact]
    public async Task ListIsBillerScoped()
    {
        var billerA = Guid.NewGuid().ToString();
        var billerB = Guid.NewGuid().ToString();
        await client.PostAsJsonAsync(
            $"billers/{billerA}/invoices/seed", new SeedInvoicesRequest(Count: 2), Wire);

        var other = await client.GetFromJsonAsync<InvoiceListResponse>(
            $"billers/{billerB}/invoices", Wire);

        Assert.NotNull(other);
        Assert.Empty(other.Invoices);
    }

    [Fact]
    public async Task UnknownInvoiceReturns404Envelope()
    {
        var response = await client.GetAsync(
            new Uri($"billers/{Guid.NewGuid()}/invoices/{Guid.NewGuid()}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"error\"", body, StringComparison.Ordinal);
        Assert.Contains("not_found", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StatusTransitionEnforcedAndIdempotent()
    {
        var billerId = Guid.NewGuid().ToString();
        var seedResponse = await client.PostAsJsonAsync(
            $"billers/{billerId}/invoices/seed", new SeedInvoicesRequest(Count: 1), Wire);
        var seeded = await seedResponse.Content.ReadFromJsonAsync<InvoiceListResponse>(Wire);
        var invoiceId = seeded!.Invoices[0].InvoiceId;

        var pay = new UpdateInvoiceStatusRequest(InvoiceStatus.Paid, "payment-1");
        var first = await client.PostAsJsonAsync(
            $"billers/{billerId}/invoices/{invoiceId}/status", pay, Wire);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Same payment replaying is idempotent.
        var replay = await client.PostAsJsonAsync(
            $"billers/{billerId}/invoices/{invoiceId}/status", pay, Wire);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);

        // A different payment against a paid invoice conflicts.
        var duplicate = await client.PostAsJsonAsync(
            $"billers/{billerId}/invoices/{invoiceId}/status",
            new UpdateInvoiceStatusRequest(InvoiceStatus.Paid, "payment-2"),
            Wire);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Contains(
            "already_paid",
            await duplicate.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }
}
