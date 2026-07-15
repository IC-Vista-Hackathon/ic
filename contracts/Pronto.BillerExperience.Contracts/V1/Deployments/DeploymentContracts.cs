namespace Pronto.BillerExperience.Contracts.V1.Deployments;

public sealed record PublishExperienceRequest(string BillerId, string Revision);

public sealed record PublishedExperienceArtifact(
    string BillerId,
    string Slug,
    string Revision,
    Experiences.BillerExperienceDefinition Definition,
    DateTimeOffset PublishedAt);

public sealed record DeploymentStatusResponse(
    string DeploymentId,
    string BillerId,
    string Revision,
    DeploymentState State,
    Uri? PublishedUrl,
    string? FailureCode,
    string? FailureMessage,
    DateTimeOffset UpdatedAt);

public enum DeploymentState
{
    Requested,
    Applying,
    WaitingForReadiness,
    Verifying,
    Ready,
    Failed,
    RolledBack
}
