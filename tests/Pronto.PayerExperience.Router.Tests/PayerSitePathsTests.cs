using Pronto.PayerExperience.Router;
using Xunit;

namespace Pronto.PayerExperience.Router.Tests;

public sealed class PayerSitePathsTests
{
    [Theory]
    [InlineData("/pay/acme", "acme", "index.html")]
    [InlineData("/pay/acme/", "acme", "index.html")]
    [InlineData("/pay/acme/assets/index-abc.js", "acme", "assets/index-abc.js")]
    [InlineData("/pay/city-of-vista/history", "city-of-vista", "history")]
    public void ParsesSlugAndRelativePath(string path, string expectedSlug, string expectedRelative)
    {
        var parsed = PayerSitePaths.Parse(path);
        Assert.NotNull(parsed);
        Assert.Equal(expectedSlug, parsed!.Value.Slug);
        Assert.Equal(expectedRelative, parsed.Value.RelativePath);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/pay")]
    [InlineData("/pay/")]
    [InlineData("/invoices/x")]
    public void RejectsNonBillerPaths(string path) => Assert.Null(PayerSitePaths.Parse(path));

    [Theory]
    [InlineData("history", true)]
    [InlineData("preferences", true)]
    [InlineData("assets/index-abc.js", false)]
    [InlineData("index.html", false)]
    public void IdentifiesSpaRoutes(string relative, bool isSpa) =>
        Assert.Equal(isSpa, PayerSitePaths.IsSpaRoute(relative));

    [Fact]
    public void BuildsBlobNameUnderSitePrefix() =>
        Assert.Equal(
            "billers/acme/revisions/rev-1/site/assets/x.js",
            PayerSitePaths.BlobName("billers/acme/revisions/rev-1/site", "assets/x.js"));

    [Theory]
    [InlineData("assets/x.js", "text/javascript; charset=utf-8")]
    [InlineData("index.html", "text/html; charset=utf-8")]
    [InlineData("styles.css", "text/css; charset=utf-8")]
    [InlineData("icon.svg", "image/svg+xml")]
    public void MapsContentTypes(string relative, string expected) =>
        Assert.Equal(expected, PayerSitePaths.ContentType(relative));

    [Fact]
    public void EntryRevalidatesWhileAssetsAreImmutable()
    {
        Assert.Contains("no-cache", PayerSitePaths.CacheControl("index.html"));
        Assert.Contains("immutable", PayerSitePaths.CacheControl("assets/x.js"));
    }

    [Fact]
    public void DerivesSitePrefixWhenPointerOmitsIt() =>
        Assert.Equal(
            "billers/acme/revisions/rev-9/site",
            new ActiveRevision("acme", "rev-9").ResolveSitePrefix());
}
