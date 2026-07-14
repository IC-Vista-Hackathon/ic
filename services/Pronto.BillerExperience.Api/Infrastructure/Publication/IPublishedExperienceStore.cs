using Pronto.BillerExperience.Contracts.V1.Deployments;

namespace Pronto.BillerExperience.Api.Infrastructure.Publication;

public interface IPublishedExperienceStore
{
    ValueTask<PublishedExperienceArtifact?> GetActiveAsync(string slug, CancellationToken cancellationToken);
    ValueTask<BinaryData?> GetManifestAsync(string slug, string revision, CancellationToken cancellationToken);
}

public sealed class UnavailablePublishedExperienceStore : IPublishedExperienceStore
{
    public ValueTask<PublishedExperienceArtifact?> GetActiveAsync(string slug, CancellationToken cancellationToken) =>
        ValueTask.FromResult<PublishedExperienceArtifact?>(null);

    public ValueTask<BinaryData?> GetManifestAsync(string slug, string revision, CancellationToken cancellationToken) =>
        ValueTask.FromResult<BinaryData?>(null);
}
