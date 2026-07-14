using IC.BillerExperience.Api.Domain;

namespace IC.BillerExperience.Api.Infrastructure.Persistence;

public interface IBillerExperienceRepository
{
    ValueTask<BillerRecord> CreateBillerAsync(BillerRecord biller, CancellationToken cancellationToken);
    ValueTask<BillerRecord?> GetBillerAsync(string billerId, CancellationToken cancellationToken);
    ValueTask<BillerRecord> SaveBillerAsync(BillerRecord biller, CancellationToken cancellationToken);
    ValueTask<ExperienceRecord?> GetLatestExperienceAsync(string billerId, CancellationToken cancellationToken);
    ValueTask<ExperienceRecord> SaveExperienceAsync(ExperienceRecord experience, string? expectedETag, CancellationToken cancellationToken);
    ValueTask<OnboardingRunRecord?> GetRunAsync(string billerId, string runId, CancellationToken cancellationToken);
    ValueTask<OnboardingRunRecord> SaveRunAsync(OnboardingRunRecord run, string? expectedETag, CancellationToken cancellationToken);
    ValueTask<DeploymentRecord?> GetDeploymentAsync(string billerId, string deploymentId, CancellationToken cancellationToken);
    ValueTask<DeploymentRecord> CreateDeploymentAsync(DeploymentRecord deployment, CancellationToken cancellationToken);
}

public sealed class ConcurrencyException(string message) : Exception(message);
