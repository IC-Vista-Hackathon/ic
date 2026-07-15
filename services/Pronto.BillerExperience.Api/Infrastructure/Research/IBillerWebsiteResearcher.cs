using Pronto.BillerExperience.Contracts.V1.Research;

namespace Pronto.BillerExperience.Api.Infrastructure.Research;

public interface IBillerWebsiteResearcher
{
    Task<BillerResearchResponse> ResearchAsync(
        BillerResearchRequest request,
        CancellationToken cancellationToken = default);
}
