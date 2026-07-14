using System.Net;
using System.Text;
using IC.Invoice.Contracts.V1.Invoices;
using IC.Payment.Api.Clients;
using IC.ServiceDefaults.Errors;
using Xunit;

namespace IC.Payment.Api.Tests;

public sealed class HttpInvoiceClientTests
{
    private const string InvoiceJson =
        """
        {"id":"i-1","biller_id":"b-1","account_number":"ACCT-1","payer_name":"Pat",
         "description":"Water","amount_cents":8420,"due_date":"2026-07-28","status":"due"}
        """;

    [Fact]
    public async Task GetParsesSnakeCaseInvoice()
    {
        var client = Client(_ => Response(HttpStatusCode.OK, InvoiceJson));

        var invoice = await client.GetAsync("b-1", "i-1", CancellationToken.None);

        Assert.NotNull(invoice);
        Assert.Equal("i-1", invoice.Id);
        Assert.Equal(8420, invoice.AmountCents);
        Assert.Equal(InvoiceStatus.Due, invoice.Status);
    }

    [Fact]
    public async Task GetReturnsNullOn404()
    {
        var client = Client(_ => Response(HttpStatusCode.NotFound, """{"error":{"code":"not_found","message":"nope"}}"""));

        Assert.Null(await client.GetAsync("b-1", "missing", CancellationToken.None));
    }

    [Fact]
    public async Task GetSurfacesDownstreamEnvelope()
    {
        var client = Client(_ => Response(
            HttpStatusCode.BadRequest, """{"error":{"code":"invalid_biller","message":"biller_id is required."}}"""));

        var exception = await Assert.ThrowsAsync<ServiceException>(
            () => client.GetAsync("b-1", "i-1", CancellationToken.None));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("invalid_biller", exception.Code);
    }

    [Fact]
    public async Task UpdateStatusSurfaces409AlreadyPaid()
    {
        var client = Client(_ => Response(
            HttpStatusCode.Conflict, """{"error":{"code":"already_paid","message":"invoice i-1 is already paid."}}"""));

        var exception = await Assert.ThrowsAsync<ServiceException>(() => client.UpdateStatusAsync(
            "b-1", "i-1", new UpdateInvoiceStatusRequest(InvoiceStatus.Paid, "p-1"), CancellationToken.None));

        Assert.Equal(409, exception.StatusCode);
        Assert.Equal("already_paid", exception.Code);
    }

    [Fact]
    public async Task NonEnvelopeErrorBodyFallsBackToGenericCode()
    {
        var client = Client(_ => Response(
            HttpStatusCode.BadGateway, "<html>proxy error</html>"));

        var exception = await Assert.ThrowsAsync<ServiceException>(
            () => client.GetAsync("b-1", "i-1", CancellationToken.None));

        Assert.Equal(502, exception.StatusCode);
        Assert.Equal("invoice_service_error", exception.Code);
    }

    [Fact]
    public async Task UpdateStatusSendsSnakeCaseBodyToStatusRoute()
    {
        HttpRequestMessage? seen = null;
        string? body = null;
        var client = Client(request =>
        {
            seen = request;
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Response(HttpStatusCode.OK, InvoiceJson.Replace("\"due\"", "\"paid\"", StringComparison.Ordinal));
        });

        await client.UpdateStatusAsync(
            "b-1", "i-1", new UpdateInvoiceStatusRequest(InvoiceStatus.Paid, "p-1"), CancellationToken.None);

        Assert.Equal("/billers/b-1/invoices/i-1/status", seen!.RequestUri!.AbsolutePath);
        Assert.Contains("\"payment_id\"", body, StringComparison.Ordinal);
        Assert.Contains("\"paid\"", body, StringComparison.Ordinal);
    }

    private static HttpInvoiceClient Client(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        new(new HttpClient(new StubHandler(responder)) { BaseAddress = new Uri("http://invoice.test/") });

    private static HttpResponseMessage Response(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
