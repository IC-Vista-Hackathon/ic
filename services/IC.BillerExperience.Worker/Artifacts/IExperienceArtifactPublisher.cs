namespace IC.BillerExperience.Worker.Artifacts;

public interface IExperienceArtifactPublisher
{
    ValueTask PublishAsync(PublicationArtifactPlan plan, CancellationToken cancellationToken);
}
