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

// End-to-end smoke test of the publish loop the F0 feature closes: the Worker builds a bundle
// (site tree uploaded under the revision's site/ prefix), flips active.json via the real
// BlobExperienceArtifactPublisher, and the real PayerSiteRouter then serves that built bundle at
// /pay/{slug}. Blob Storage is replaced by a shared in-memory store; the builder is faked to write
// the site tree the way the Kubernetes build Job would. This locks the blob-prefix contract across
// builder -> Worker active.json -> Router (which derives the site prefix from the revision).
public sealed class PublishToRouterSmokeTests
{
    private const string Slug = "city";
    private const string IndexHtml = "<!doctype html><title>City Pay</title><div id=root>built</div>";
    private const string AssetJs = "console.log('city-pay-bundle');";
    private const string AssetPath = "assets/app-abc123.js";

    [Fact]
    public async Task PublishBuildsBundleThatRouterServesAtPayPath()
    {
        var store = new InMemoryBlobStore();
        var container = new InMemoryBlobContainerClient(store);
        var options = Options.Create(new PublicationOptions
        {
            PublicBaseUrl = "https://pay.example.test",
            StorageEndpoint = "https://blob.example.test/",
            ContainerName = "payer-experiences",
        });

        var repository = new SmokeRepository();
        var publisher = new BlobExperienceArtifactPublisher(container, NullLogger<BlobExperienceArtifactPublisher>.Instance);
        var builder = new SiteWritingBundleBuilder(store);
        var processor = new PublicationProcessor(
            repository,
            new PublicationArtifactPlanFactory(options),
            publisher,
            builder,
            options,
            NullLogger<PublicationProcessor>.Instance);

        await processor.ProcessAsync(repository.Deployment, CancellationToken.None);

        // The publish must have completed and pointed the payer URL at the router route.
        Assert.Equal(PublicationStates.Ready, repository.Saved?.Status);
        Assert.Equal(new Uri("https://pay.example.test/pay/city/"), repository.Saved?.PublishedUrl);
        Assert.True(store.TryGet("billers/city/active.json", out _));

        var router = new PayerSiteRouter(
            container,
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new RouterOptions()),
            NullLogger<PayerSiteRouter>.Instance);

        // Root -> built index.html served from the revision the Worker activated.
        Assert.Equal((StatusCodes.Status200OK, IndexHtml), await GetAsync(router, "/pay/city"));
        // Hashed asset -> served with the js content type.
        var (assetStatus, assetBody) = await GetAsync(router, "/pay/city/assets/app-abc123.js");
        Assert.Equal(StatusCodes.Status200OK, assetStatus);
        Assert.Equal(AssetJs, assetBody);
        // Client-side route with no extension -> SPA fallback to index.html.
        Assert.Equal((StatusCodes.Status200OK, IndexHtml), await GetAsync(router, "/pay/city/invoices/42"));
        // Unknown biller -> 404 (no active.json).
        var (missingStatus, _) = await GetAsync(router, "/pay/unknown");
        Assert.Equal(StatusCodes.Status404NotFound, missingStatus);
    }

    private static async Task<(int Status, string Body)> GetAsync(PayerSiteRouter router, string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        var body = new MemoryStream();
        context.Response.Body = body;

        await router.HandleAsync(context);

        body.Position = 0;
        using var reader = new StreamReader(body);
        return (context.Response.StatusCode, await reader.ReadToEndAsync());
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
            store.Put($"{sitePrefix}/index.html", Encoding.UTF8.GetBytes(IndexHtml), "text/html; charset=utf-8");
            store.Put($"{sitePrefix}/{AssetPath}", Encoding.UTF8.GetBytes(AssetJs), "text/javascript; charset=utf-8");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SmokeRepository : IPublicationRepository
    {
        public PublicationDeployment Deployment { get; } = new(
            "deployment-1", "biller-1", 1, PublicationStates.Applying, DateTimeOffset.UtcNow, ETag: "etag");
        public PublicationDeployment? Saved { get; private set; }
        public bool? WorkflowPublished { get; private set; }

        public ValueTask<PublicationDeployment?> ClaimNextAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<PublicationDeployment?>(Deployment);

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
