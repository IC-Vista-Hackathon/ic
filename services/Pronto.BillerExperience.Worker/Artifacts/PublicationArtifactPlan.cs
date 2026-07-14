using Pronto.BillerExperience.Contracts.V1.Deployments;

namespace Pronto.BillerExperience.Worker.Artifacts;

public sealed record PublicationArtifactPlan(
    string BillerId,
    string Slug,
    string Revision,
    string RevisionPrefix,
    string ActiveBlobName,
    string ConfigJson,
    string ManifestJson,
    Uri PublishedUrl,
    PublishedExperienceArtifact Artifact);
