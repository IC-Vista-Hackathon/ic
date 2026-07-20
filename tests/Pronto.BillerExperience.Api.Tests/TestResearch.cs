using Pronto.BillerExperience.Api.Infrastructure.Research;
using Pronto.BillerExperience.Contracts.V1.Research;

namespace Pronto.BillerExperience.Api.Tests;

// Shared research doubles. The bootstrap draft is unbranded until research runs, so tests that
// approve or publish a draft supply a completed research result carrying first-party brand
// evidence — the same path production takes to make a draft publishable.
internal static class TestResearch
{
    public static BillerResearchResponse BrandEvidence(string website = "https://vista.example")
    {
        var source = new Uri(website.EndsWith('/') ? website : website + "/");
        return new BillerResearchResponse(
            ResearchOutcome.Completed,
            [
                new ResearchFact(BrandEvidenceFacts.PrimaryColor, "#174a5b", source, 0.9),
                new ResearchFact(BrandEvidenceFacts.SecondaryColor, "#18b4e9", source, 0.8),
                new ResearchFact(BrandEvidenceFacts.LogoUrl, source.AbsoluteUri + "logo.png", source, 0.9),
                new ResearchFact(BrandEvidenceFacts.Tagline, "Serving our community.", source, 0.7)
            ],
            [new ResearchSource(source, "Research", DateTimeOffset.UtcNow)],
            []);
    }

    public sealed class StubCoordinator(BillerResearchResponse response) : IBillerResearchCoordinator
    {
        public BillerResearchRequest? Request { get; private set; }

        public Task<BillerResearchResponse> ResearchAsync(
            BillerResearchRequest request,
            ResearchExecutionContext? executionContext = null,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(response);
        }
    }
}
