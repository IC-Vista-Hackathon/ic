using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pronto.BillerExperience.Contracts.V1.Experiences;
using Pronto.BillerExperience.Worker.Artifacts;
using Pronto.BillerExperience.Worker.Building;
using Pronto.BillerExperience.Worker.Persistence;
using Pronto.PayerExperience.Router;
using Xunit;

namespace Pronto.BillerExperience.Worker.Tests;

// End-to-end smoke tests of the publish loop the F0 feature closes: the Worker builds a bundle
// (site tree uploaded under the revision's site/ prefix), flips active.json via the real
// BlobExperienceArtifactPublisher, and the real PayerSiteRouter then serves that built bundle at
// /pay/{slug}. Blob Storage is replaced by a shared in-memory store; the builder is faked to write
// the site tree the way the Kubernetes build Job would. This locks the blob-prefix contract across
// builder -> Worker active.json -> Router (which derives the site prefix from the revision).
public sealed class PublishToRouterSmokeTests
{
    private const string Slug = "city";
    private const string AssetPath = "assets/app-abc123.js";

    private static string IndexHtmlFor(string revision) =>
        $"<!doctype html><title>City Pay</title><div id=root data-revision=\"{revision}\">built</div>";

    private static string AssetJsFor(string revision) => $"console.log('city-pay-bundle {revision}');";

    [Fact]
    public async Task PublishBuildsBundleThatRouterServesAtPayPath()
    {
        var harness = new Harness();

        await harness.PublishAsync(version: 1);

        // The publish must have completed and pointed the payer URL at the router route.
        Assert.Equal(PublicationStates.Ready, harness.Repository.Saved?.Status);
        Assert.Equal(new Uri("https://pay.example.test/pay/city/"), harness.Repository.Saved?.PublishedUrl);
        Assert.True(harness.Store.TryGet("billers/city/active.json", out _));

        var router = harness.NewRouter();

        // Root -> built index.html served from the revision the Worker activated.
        var (rootStatus, rootBody, _, _) = await GetAsync(router, "/pay/city");
        Assert.Equal(StatusCodes.Status200OK, rootStatus);
        Assert.Equal(IndexHtmlFor("config-1"), rootBody);
        // Hashed asset -> served with the js content type.
        var (assetStatus, assetBody, _, _) = await GetAsync(router, "/pay/city/assets/app-abc123.js");
        Assert.Equal(StatusCodes.Status200OK, assetStatus);
        Assert.Equal(AssetJsFor("config-1"), assetBody);
        // Client-side route with no extension -> SPA fallback to index.html.
        var (spaStatus, spaBody, _, _) = await GetAsync(router, "/pay/city/invoices/42");
        Assert.Equal(StatusCodes.Status200OK, spaStatus);
        Assert.Equal(IndexHtmlFor("config-1"), spaBody);
        // Unknown biller -> 404 (no active.json).
        var (missingStatus, _, _, _) = await GetAsync(router, "/pay/unknown");
        Assert.Equal(StatusCodes.Status404NotFound, missingStatus);
    }

    [Fact]
    public async Task ServedBundleCarriesCorrectContentTypeAndCacheHeaders()
    {
        var harness = new Harness();
        await harness.PublishAsync(version: 1);
        var router = harness.NewRouter();

        var (_, _, indexType, indexCache) = await GetAsync(router, "/pay/city");
        Assert.Equal("text/html; charset=utf-8", indexType);
        // The SPA shell must revalidate across revision cutovers, so it is never cached immutably.
        Assert.Equal("no-cache, no-store, must-revalidate", indexCache);

        var (_, _, assetType, assetCache) = await GetAsync(router, "/pay/city/assets/app-abc123.js");
        Assert.Equal("text/javascript; charset=utf-8", assetType);
        // Content-addressed (hashed) build assets are immutable and cached for a year.
        Assert.Equal("public, max-age=31536000, immutable", assetCache);
    }

    [Fact]
    public async Task RepublishingNewRevisionAtomicallyCutsRouterOverToRebuiltBundle()
    {
        var harness = new Harness();
        await harness.PublishAsync(version: 1);
        await harness.PublishAsync(version: 2);

        Assert.Equal(PublicationStates.Ready, harness.Repository.Saved?.Status);

        // A fresh router (cold active-pointer cache) resolves the newly activated revision and
        // serves the rebuilt bundle; both revisions' immutable site trees remain in storage.
        var router = harness.NewRouter();
        var (status, body, _, _) = await GetAsync(router, "/pay/city");
        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.Equal(IndexHtmlFor("config-2"), body);
        Assert.True(harness.Store.TryGet("billers/city/revisions/config-1/site/index.html", out _));
        Assert.True(harness.Store.TryGet("billers/city/revisions/config-2/site/index.html", out _));
    }

    [Fact]
    public async Task FailedRebuildLeavesPreviousRevisionLiveAndServed()
    {
        var harness = new Harness();
        await harness.PublishAsync(version: 1);

        // A rebuild that fails during the build step must NOT flip active.json.
        await harness.PublishAsync(version: 2, builder: new FailingBundleBuilder());

        Assert.Equal(PublicationStates.Failed, harness.Repository.Saved?.Status);
        Assert.Equal("BUNDLE_BUILD_FAILED", harness.Repository.Saved?.FailureCode);
        // active.json still points at revision 1, and the router keeps serving the previous bundle.
        var router = harness.NewRouter();
        var (status, body, _, _) = await GetAsync(router, "/pay/city");
        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.Equal(IndexHtmlFor("config-1"), body);
        // The failed revision's site tree was never activated (build never ran).
        Assert.False(harness.Store.TryGet("billers/city/revisions/config-2/site/index.html", out _));
    }

    private static async Task<(int Status, string Body, string? ContentType, string CacheControl)> GetAsync(
        PayerSiteRouter router,
        string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        var body = new MemoryStream();
        context.Response.Body = body;

        await router.HandleAsync(context);

        body.Position = 0;
        using var reader = new StreamReader(body);
        return (
            context.Response.StatusCode,
            await reader.ReadToEndAsync(),
            context.Response.ContentType,
            context.Response.Headers.CacheControl.ToString());
    }

    // Wires the real publisher + processor over a shared in-memory blob store, one instance per test.
    private sealed class Harness
    {
        public InMemoryBlobStore Store { get; } = new();
        public SmokeRepository Repository { get; } = new();

        private InMemoryBlobContainerClient Container => new(Store);

        private static readonly IOptions<PublicationOptions> Options = Microsoft.Extensions.Options.Options.Create(
            new PublicationOptions
            {
                PublicBaseUrl = "https://pay.example.test",
                StorageEndpoint = "https://blob.example.test/",
                ContainerName = "payer-experiences",
            });

        public async Task PublishAsync(int version, IExperienceBundleBuilder? builder = null)
        {
            var processor = new PublicationProcessor(
                Repository,
                new PublicationArtifactPlanFactory(Options),
                new BlobExperienceArtifactPublisher(Container, NullLogger<BlobExperienceArtifactPublisher>.Instance),
                builder ?? new SiteWritingBundleBuilder(Store),
                Options,
                NullLogger<PublicationProcessor>.Instance);

            await processor.ProcessAsync(SmokeRepository.DeploymentFor(version), CancellationToken.None);
        }

        public PayerSiteRouter NewRouter() => new(
            Container,
            new MemoryCache(new MemoryCacheOptions()),
            Microsoft.Extensions.Options.Options.Create(new RouterOptions()),
            NullLogger<PayerSiteRouter>.Instance);
    }

    // Mirrors the builder's publish step: writes the built site tree under the revision's site/
    // prefix (billers/{slug}/revisions/{revision}/site) without touching active.json, exactly as
    // the Kubernetes build Job does when PAYER_SKIP_ACTIVE=true.
    private sealed class SiteWritingBundleBuilder(InMemoryBlobStore store) : IExperienceBundleBuilder
    {
        public bool Enabled => true;

        public ValueTask BuildAsync(BundleBuildRequest request, CancellationToken cancellationToken)
        {
            var sitePrefix = $"billers/{request.Slug}/revisions/{request.Revision}/site";
            store.Put($"{sitePrefix}/index.html", Encoding.UTF8.GetBytes(IndexHtmlFor(request.Revision)), "text/html; charset=utf-8");
            store.Put($"{sitePrefix}/{AssetPath}", Encoding.UTF8.GetBytes(AssetJsFor(request.Revision)), "text/javascript; charset=utf-8");
            return ValueTask.CompletedTask;
        }
    }

    // Fails during the build step the way a rejected generate/build/validate would, so the
    // processor never reaches the active.json flip.
    private sealed class FailingBundleBuilder : IExperienceBundleBuilder
    {
        public bool Enabled => true;

        public ValueTask BuildAsync(BundleBuildRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromException(new BundleBuildException("vite build failed"));
    }

    private sealed class SmokeRepository : IPublicationRepository
    {
        public PublicationDeployment? Saved { get; private set; }
        public bool? WorkflowPublished { get; private set; }

        public static PublicationDeployment DeploymentFor(int version) => new(
            $"deployment-{version}", "biller-1", version, PublicationStates.Applying, DateTimeOffset.UtcNow, ETag: "etag");

        public ValueTask<PublicationDeployment?> ClaimNextAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<PublicationDeployment?>(DeploymentFor(1));

        public ValueTask<PublicationBiller> GetBillerAsync(string billerId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PublicationBiller(billerId, "City", Slug));

        public ValueTask<PublicationExperience> GetExperienceAsync(string billerId, int version, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PublicationExperience($"config-{version}", billerId, version, Definition(billerId)));

        public ValueTask<PublicationDeployment> SaveAsync(PublicationDeployment deployment, CancellationToken cancellationToken)
        {
            Saved = deployment;
            return ValueTask.FromResult(deployment);
        }

        public ValueTask MarkWorkflowAsync(string billerId, int version, bool published, CancellationToken cancellationToken)
        {
            WorkflowPublished = published;
            return ValueTask.CompletedTask;
        }

        private static BillerExperienceDefinition Definition(string billerId) => new(
            "1.0", billerId,
            new ExperienceBrand("City", "#085368", "#18B4E9", null, "Inter"),
            new ExperienceContent("Pay", "Welcome", "Support", new Uri("https://example.test/privacy"), new Uri("https://example.test/terms")),
            new PwaConfiguration("City Pay", "City", "#085368", "#FFFFFF", null), ["card"]);
    }
}
