using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Pronto.Payment.Api.Clients;
using Xunit;

namespace Pronto.Payment.Api.Tests;

public sealed class HttpBillerConfigClientTests
{
    private static string ConfigJson(string feeHandling) =>
        "{\"definition\":{\"preferences\":{\"fee_handling\":\"" + feeHandling + "\"}}}";

    [Theory]
    [InlineData("absorb", false)]
    [InlineData("undecided", false)]
    [InlineData("charge", true)]
    [InlineData("mixed", true)]
    public async Task MapsFeeHandlingToPayerPaysFee(string feeHandling, bool expectedPayerPaysFee)
    {
        var client = Client(_ => Response(HttpStatusCode.OK, ConfigJson(feeHandling)));

        var config = await client.GetAsync("b-1", CancellationToken.None);

        Assert.Equal(expectedPayerPaysFee, config.PayerPaysFee);
    }

    [Fact]
    public async Task FallsBackToDemoDefaultsOn404()
    {
        var client = Client(_ => Response(HttpStatusCode.NotFound, "{}"));

        var config = await client.GetAsync("b-1", CancellationToken.None);

        Assert.True(config.PayerPaysFee);
    }

    [Fact]
    public async Task FallsBackToDemoDefaultsOnServerError()
    {
        var client = Client(_ => Response(HttpStatusCode.InternalServerError, "boom"));

        var config = await client.GetAsync("b-1", CancellationToken.None);

        Assert.True(config.PayerPaysFee);
    }

    [Fact]
    public async Task ResolvesLiveBillerForPreviewTenant()
    {
        HttpRequestMessage? seen = null;
        var client = Client(request =>
        {
            seen = request;
            return Response(HttpStatusCode.OK, ConfigJson("absorb"));
        });

        await client.GetAsync("preview-b-1", CancellationToken.None);

        Assert.Equal("/billers/b-1/config", seen!.RequestUri!.AbsolutePath);
    }

    private static HttpBillerConfigClient Client(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        new(
            new HttpClient(new StubHandler(responder)) { BaseAddress = new Uri("http://biller.test/") },
            NullLogger<HttpBillerConfigClient>.Instance);

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
