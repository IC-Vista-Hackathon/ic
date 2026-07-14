using Pronto.BillerExperience.Api.Controllers;
using Pronto.BillerExperience.Api.Infrastructure.Publication;
using Pronto.BillerExperience.Contracts.V1.Deployments;
using Pronto.BillerExperience.Contracts.V1.Experiences;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Pronto.BillerExperience.Api.Tests;

public sealed class PublishedExperiencesControllerTests
{
    [Fact]
    public async Task GetReturnsActiveDefinitionWithoutArtifactMetadata()
    {
        var definition = Definition();
        var controller = Controller(new FakeStore(new("biller-1", "city", "config-2", definition, DateTimeOffset.UtcNow)));

        var result = await controller.Get("city", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(definition, ok.Value);
        Assert.Equal("no-cache, no-store, must-revalidate", controller.Response.Headers.CacheControl);
    }

    [Fact]
    public async Task GetManifestUsesActiveRevision()
    {
        var store = new FakeStore(new("biller-1", "city", "config-7", Definition(), DateTimeOffset.UtcNow));
        var controller = Controller(store);

        var result = await controller.GetManifest("city", CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/manifest+json", file.ContentType);
        Assert.Equal("config-7", store.RequestedRevision);
    }

    [Fact]
    public async Task GetReturnsNotFoundWhenBillerHasNoActiveArtifact()
    {
        var controller = Controller(new FakeStore(null));

        var result = await controller.Get("missing", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    private static PublishedExperiencesController Controller(IPublishedExperienceStore store)
    {
        var controller = new PublishedExperiencesController(store, NullLogger<PublishedExperiencesController>.Instance);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    private static BillerExperienceDefinition Definition() => new(
        "1.0", "biller-1",
        new ExperienceBrand("City", "#085368", "#18B4E9", null, "Inter"),
        new ExperienceContent("Pay", "Welcome", "Support", new Uri("https://example.test/privacy"), new Uri("https://example.test/terms")),
        new PwaConfiguration("City Pay", "City", "#085368", "#FFFFFF", null), ["card"]);

    private sealed class FakeStore(PublishedExperienceArtifact? artifact) : IPublishedExperienceStore
    {
        public string? RequestedRevision { get; private set; }

        public ValueTask<PublishedExperienceArtifact?> GetActiveAsync(string slug, CancellationToken cancellationToken) =>
            ValueTask.FromResult(artifact);

        public ValueTask<BinaryData?> GetManifestAsync(string slug, string revision, CancellationToken cancellationToken)
        {
            RequestedRevision = revision;
            return ValueTask.FromResult<BinaryData?>(BinaryData.FromString("{}"));
        }
    }
}
