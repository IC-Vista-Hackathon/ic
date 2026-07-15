using System.Net;
using System.Text;
using IC.BillerExperience.Api.Configuration;
using IC.BillerExperience.Api.Infrastructure.Research;
using IC.BillerExperience.Contracts.V1.Research;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace IC.BillerExperience.Api.Tests;

public sealed class HttpBillerWebsiteResearcherTests
{
    [Fact]
    public async Task ResearchExtractsBoundedFactsAndCitationsFromSameHost()
    {
        var handler = new FakeHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/" => Html("<html><head><title>Example Utility</title><meta name=\"description\" content=\"Safe &amp; reliable payments\"></head><body><a href=\"/about\">About</a><a href=\"https://other.example/bad\">Off site</a></body></html>"),
            "/about" => Html("<html><head><title>About Us</title></head></html>"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var researcher = Create(handler);

        var response = await researcher.ResearchAsync(new BillerResearchRequest(new Uri("https://example.com/"), "brand", 2));

        Assert.Equal(ResearchOutcome.Completed, response.Outcome);
        Assert.Equal(2, response.Sources.Count);
        Assert.Contains(response.Facts, fact => fact.Name == "page_title" && fact.Value == "Example Utility");
        Assert.Contains(response.Facts, fact => fact.Name == "page_description" && fact.Value == "Safe & reliable payments");
        Assert.DoesNotContain(handler.Requests, uri => uri.Host == "other.example");
    }

    [Fact]
    public async Task ResearchRejectsOffDomainRedirect()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Redirect)
        {
            Headers = { Location = new Uri("https://other.example/") }
        });

        var response = await Create(handler).ResearchAsync(Request());

        Assert.Equal(ResearchOutcome.Failed, response.Outcome);
        Assert.Equal("research.off_domain", response.ErrorCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ResearchRejectsPrivateDestinationBeforeSending()
    {
        var handler = new FakeHandler(_ => Html("unused"));
        var researcher = Create(handler, new StaticResolver(IPAddress.Parse("169.254.169.254")));

        var response = await researcher.ResearchAsync(Request());

        Assert.Equal("research.unsafe_target", response.ErrorCode);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("application/json", "{}", "research.unsupported_content_type")]
    [InlineData("text/html", "01234567890123456789", "research.response_too_large")]
    public async Task ResearchRejectsUnsupportedOrOversizedContent(string mediaType, string body, string expected)
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType)
        });
        var researcher = Create(handler, maxBytes: 10);

        var response = await researcher.ResearchAsync(Request());

        Assert.Equal(expected, response.ErrorCode);
    }

    [Fact]
    public async Task ResearchMapsTransportFailureWithoutLeakingDetails()
    {
        var researcher = Create(new FakeHandler(_ => throw new HttpRequestException("secret transport detail")));

        var response = await researcher.ResearchAsync(Request());

        Assert.Equal("research.request_failed", response.ErrorCode);
        Assert.True(response.Retryable);
        Assert.DoesNotContain(response.Warnings, warning => warning.Contains("secret", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ResearchMapsCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var response = await Create(new FakeHandler(_ => Html("unused")))
            .ResearchAsync(Request(), cancellation.Token);

        Assert.Equal("research.cancelled", response.ErrorCode);
        Assert.False(response.Retryable);
    }

    private static BillerResearchRequest Request() => new(new Uri("https://example.com/"), "brand", 1);

    private static HttpBillerWebsiteResearcher Create(
        HttpMessageHandler handler,
        IDestinationAddressResolver? resolver = null,
        int maxBytes = 1000)
    {
        var options = Options.Create(new BillerExperienceOptions
        {
            Research = new ResearchOptions { MaxPages = 3, MaxLinksPerPage = 5, MaxResponseBytes = maxBytes }
        });
        return new HttpBillerWebsiteResearcher(
            new HttpClient(handler),
            resolver ?? new StaticResolver(IPAddress.Parse("93.184.216.34")),
            options,
            NullLogger<HttpBillerWebsiteResearcher>.Instance);
    }

    private static HttpResponseMessage Html(string html) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(html, Encoding.UTF8, "text/html")
    };

    private sealed class StaticResolver(params IPAddress[] addresses) : IDestinationAddressResolver
    {
        public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IPAddress>>(addresses);
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request.RequestUri!);
            return Task.FromResult(response(request));
        }
    }
}
