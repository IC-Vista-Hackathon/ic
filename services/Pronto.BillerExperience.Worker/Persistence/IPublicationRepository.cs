namespace Pronto.BillerExperience.Worker.Persistence;

public interface IPublicationRepository
{
    ValueTask<PublicationDeployment?> ClaimNextAsync(CancellationToken cancellationToken);
    ValueTask<PublicationBiller> GetBillerAsync(string billerId, CancellationToken cancellationToken);
    ValueTask<PublicationExperience> GetExperienceAsync(string billerId, int version, CancellationToken cancellationToken);
    ValueTask<PublicationDeployment> SaveAsync(PublicationDeployment deployment, CancellationToken cancellationToken);
    ValueTask MarkWorkflowAsync(string billerId, int version, bool published, CancellationToken cancellationToken);
}
