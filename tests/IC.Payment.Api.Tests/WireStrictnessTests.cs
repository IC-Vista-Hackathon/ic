using System.Net;
using System.Text;
using System.Text.Json;
using IC.Invoice.Contracts.V1.Invoices;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace IC.Payment.Api.Tests;

/// <summary>Wire must reject integer enum tokens — {"plan":99} used to 201 and echo 99.</summary>
public sealed class WireStrictnessTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions SnakeWire = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly HttpClient client;

    public WireStrictnessTests(WebApplicationFactory<Program> factory)
    {
        client = factory.CreateClient();
    }

    [Fact]
    public async Task IntegerPlanTokenRejected()
    {
        var billerId = Guid.NewGuid().ToString();
        using var body = new StringContent(
            $$"""{"biller_id":"{{billerId}}","plan":99}""", Encoding.UTF8, "application/json");

        var response = await client.PostAsync(new Uri("purchases", UriKind.Relative), body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task IntegerPaymentStatusNeverSerialized()
    {
        // Contract-level converter: InvoiceStatus rejects integer tokens in any host.
        Assert.ThrowsAny<JsonException>(() =>
            JsonSerializer.Deserialize<UpdateInvoiceStatusRequest>(
                """{"status":1,"payment_id":"p-1"}""", SnakeWire));
    }
}
