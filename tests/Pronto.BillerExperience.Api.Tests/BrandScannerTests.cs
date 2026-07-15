using System.Net;
using System.Text;
using Pronto.BillerExperience.Api.Configuration;
using Pronto.BillerExperience.Api.Infrastructure.Research;
using Pronto.BillerExperience.Contracts.V1.Branding;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Pronto.BillerExperience.Api.Tests;

public sealed class BrandScannerTests
{
    private const string HomePage = """
        <html><head>
        <meta name="theme-color" content="#1a73e8">
        <link rel="apple-touch-icon" href="/apple-touch-icon.png">
        <link rel="icon" href="/favicon.ico">
        <link rel="stylesheet" href="/styles.css">
        <link href="https://fonts.googleapis.com/css2?family=Poppins:wght@400;700&display=swap" rel="stylesheet">
        <meta property="og:image" content="https://cdn.example.com/social.png">
        <style>.hero{color:#c2185b;background:#1a73e8}</style>
        </head><body></body></html>
        """;

    private const string StyleSheet = """
        body{font-family:'Poppins', Helvetica, sans-serif;color:#c2185b}
        .btn{background:#00897b;color:#ffffff}
        .btn:hover{background:#00897b}
        """;

    [Fact]
    public async Task ScanExtractsColorsFontAndLogoFromWebsiteAndCss()
    {
        var handler = new FakeHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/" => Html(HomePage),
            "/styles.css" => Css(StyleSheet),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });

        var response = await Scan(handler, new BrandScanRequest(new Uri("https://example.com/")));

        Assert.Equal(BrandScanOutcome.Completed, response.Outcome);
        Assert.Equal("#1a73e8", response.PrimaryColor);
        Assert.Contains("#c2185b", response.Palette);
        Assert.Contains("#00897b", response.Palette);
        Assert.DoesNotContain("#ffffff", response.Palette);
        Assert.Equal("Poppins", response.FontFamily);
        Assert.Equal("https://example.com/apple-touch-icon.png", response.LogoUrl!.ToString());
    }

    [Fact]
    public async Task ScanReadsRgbColorsAndFontFromStylesheetOnly()
    {
        var handler = new FakeHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/" => Html("""<html><head><link rel="stylesheet" href="/app.css"></head></html>"""),
            "/app.css" => Css("h1{font-family:\"Merriweather\",serif}a{color:rgb(214, 40, 40)}"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });

        var response = await Scan(handler, new BrandScanRequest(new Uri("https://example.com/")));

        Assert.Equal("Merriweather", response.FontFamily);
        Assert.Contains("#d62828", response.Palette);
    }

    [Fact]
    public async Task ScanRejectsNonHttpsWithoutSendingRequest()
    {
        var handler = new FakeHandler(_ => Html(HomePage));

        var response = await Scan(handler, new BrandScanRequest(new Uri("http://example.com/")));

        Assert.Equal(BrandScanOutcome.Failed, response.Outcome);
        Assert.Equal("brand_scan.https_required", response.ErrorCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ScanReportsFailureWhenHomepageUnreachable()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var response = await Scan(handler, new BrandScanRequest(new Uri("https://example.com/")));

        Assert.Equal(BrandScanOutcome.Failed, response.Outcome);
        Assert.Equal("brand_scan.http_error", response.ErrorCode);
    }

    [Fact]
    public async Task ScanFallsBackToFaviconWhenNoIconsDeclared()
    {
        var handler = new FakeHandler(_ => Html("<html><head><title>No icons</title></head></html>"));

        var response = await Scan(handler, new BrandScanRequest(new Uri("https://example.com/")));

        Assert.Equal("https://example.com/favicon.ico", response.LogoUrl!.ToString());
    }

    [Fact]
    public async Task ScanDegradesInsteadOfFailingWhenAStylesheetTimesOut()
    {
        var handler = new FakeHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/styles.css")
            {
                throw new TaskCanceledException("simulated per-request timeout");
            }

            return Html(HomePage);
        });

        var response = await Scan(handler, new BrandScanRequest(new Uri("https://example.com/")));

        Assert.Equal(BrandScanOutcome.Degraded, response.Outcome);
        Assert.Contains("brand_scan.stylesheet_unreadable", response.Warnings);
        Assert.Equal("#1a73e8", response.PrimaryColor);
        Assert.Equal("https://example.com/apple-touch-icon.png", response.LogoUrl!.ToString());
    }

    [Fact]
    public async Task ScanTruncatesOversizedHomepageInsteadOfFailing()
    {
        var oversized = "<html><head>"
            + "<meta name=\"theme-color\" content=\"#1a73e8\">"
            + "<link rel=\"apple-touch-icon\" href=\"/apple-touch-icon.png\">"
            + "</head><body>"
            + new string('x', 400_000)
            + "</body></html>";
        var handler = new FakeHandler(_ => Html(oversized));

        var response = await Scan(handler, new BrandScanRequest(new Uri("https://example.com/")));

        Assert.NotEqual(BrandScanOutcome.Failed, response.Outcome);
        Assert.Equal("#1a73e8", response.PrimaryColor);
        Assert.Equal("https://example.com/apple-touch-icon.png", response.LogoUrl!.ToString());
    }

    private static async Task<BrandScanResponse> Scan(HttpMessageHandler handler, BrandScanRequest request)
    {
        var options = Options.Create(new BillerExperienceOptions
        {
            Research = new ResearchOptions { MaxResponseBytes = 200_000, RequestTimeoutSeconds = 5 }
        });
        using var client = new HttpClient(handler);
        var scanner = new HttpBrandScanner(client, options, NullLogger<HttpBrandScanner>.Instance);
        return await scanner.ScanAsync(request);
    }

    private static HttpResponseMessage Html(string html) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(html, Encoding.UTF8, "text/html")
    };

    private static HttpResponseMessage Css(string css) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(css, Encoding.UTF8, "text/css")
    };

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
