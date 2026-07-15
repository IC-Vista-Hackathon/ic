namespace Pronto.BillerExperience.Worker.Building;

// Input for a single per-biller bundle build. DefinitionJson is the builder-shaped
// (snake_case) ExperienceDefinition; Revision must match the config publisher's revision so
// the router resolves billers/{slug}/revisions/{revision}/site from the active pointer.
public sealed record BundleBuildRequest(
    string BillerId,
    string Slug,
    string Revision,
    string DefinitionJson,
    string StorageEndpoint,
    string ContainerName);

public interface IExperienceBundleBuilder
{
    // True when a build backend is configured; false means the caller should skip the
    // bundle step and publish config only.
    bool Enabled { get; }

    // Runs the build to completion. Throws if generation, build, validation, or upload fails —
    // the caller then leaves the previous revision active (no active.json flip happens).
    ValueTask BuildAsync(BundleBuildRequest request, CancellationToken cancellationToken);
}
