using Pronto.BillerExperience.Api.Domain;
using Pronto.BillerExperience.Contracts.V1.Onboarding;

namespace Pronto.BillerExperience.Api.Infrastructure.Persistence;

public interface IBillerExperienceRepository
{
    ValueTask<BillerRecord> CreateBillerAsync(BillerRecord biller, CancellationToken cancellationToken);
    ValueTask<BillerRecord?> GetBillerAsync(string billerId, CancellationToken cancellationToken);
    ValueTask<BillerRecord> SaveBillerAsync(BillerRecord biller, CancellationToken cancellationToken);
    ValueTask<ExperienceRecord?> GetLatestExperienceAsync(string billerId, CancellationToken cancellationToken);
    ValueTask<ExperienceRecord> SaveExperienceAsync(ExperienceRecord experience, string? expectedETag, CancellationToken cancellationToken);
    ValueTask<OnboardingRunRecord?> GetRunAsync(string billerId, string runId, CancellationToken cancellationToken);
    ValueTask<OnboardingRunRecord> SaveRunAsync(OnboardingRunRecord run, string? expectedETag, CancellationToken cancellationToken);
    ValueTask AppendAgentActivityAsync(string billerId, string runId, AgentActivityEvent activity, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<AgentActivityEvent>> GetAgentActivityAsync(string billerId, string runId, CancellationToken cancellationToken);
    ValueTask<AgentContextRecord?> GetAgentContextAsync(string billerId, string runId, CancellationToken cancellationToken);
    ValueTask<AgentContextRecord> SaveAgentContextAsync(AgentContextRecord context, string? expectedETag, CancellationToken cancellationToken);
    ValueTask<DeploymentRecord?> GetDeploymentAsync(string billerId, string deploymentId, CancellationToken cancellationToken);
    ValueTask<DeploymentRecord> CreateDeploymentAsync(DeploymentRecord deployment, CancellationToken cancellationToken);

    /// <summary>
    /// Delete a biller and all of its experiences, runs, and deployments. Test-cleanup support
    /// for functional tests; exposed only through the nonprod-gated maintenance endpoint.
    /// </summary>
    ValueTask PurgeByBillerAsync(string billerId, CancellationToken cancellationToken);
}

public sealed class ConcurrencyException(string message) : Exception(message);
