using System.Diagnostics;
using System.Net;
using Pronto.ServiceDefaults;
using Xunit;

namespace Pronto.Payment.Api.Tests;

public sealed class CorrelationPropagationHandlerTests
{
    [Fact]
    public async Task CopiesCorrelationTagsFromActivityToOutboundHeaders()
    {
        using var activity = new Activity("test").Start();
        activity.SetTag("ic.correlation_id", "corr-123");
        activity.SetTag("ic.biller_id", "biller-9");
        HttpRequestMessage? seen = null;
        using var client = Client(request => seen = request);

        await client.GetAsync(new Uri("http://downstream.test/x"));

        Assert.Equal("corr-123", Assert.Single(seen!.Headers.GetValues(RequestObservabilityMiddleware.CorrelationHeader)));
        Assert.Equal("biller-9", Assert.Single(seen.Headers.GetValues(RequestObservabilityMiddleware.BillerHeader)));
    }

    [Fact]
    public async Task NoActivityMeansNoHeaders()
    {
        Activity.Current = null;
        HttpRequestMessage? seen = null;
        using var client = Client(request => seen = request);

        await client.GetAsync(new Uri("http://downstream.test/x"));

        Assert.False(seen!.Headers.Contains(RequestObservabilityMiddleware.CorrelationHeader));
        Assert.False(seen.Headers.Contains(RequestObservabilityMiddleware.BillerHeader));
    }

    private static HttpClient Client(Action<HttpRequestMessage> capture) =>
        new(new CorrelationPropagationHandler { InnerHandler = new StubHandler(capture) });

    private sealed class StubHandler(Action<HttpRequestMessage> capture) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            capture(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
