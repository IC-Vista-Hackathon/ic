using IC.BillerExperience.Contracts.V1.Research;

namespace IC.BillerExperience.Api.Infrastructure.Research;

public interface IBillerWebsiteResearcher
{
    Task<BillerResearchResponse> ResearchAsync(
        BillerResearchRequest request,
        CancellationToken cancellationToken = default);
}
