namespace Pronto.BillerExperience.Worker.Building;

// Configuration for the per-biller bundle build Job the Worker launches on Kubernetes.
// When BuilderImage is empty the bundle-build step is skipped and the Worker falls back to
// config-only publication (its prior behavior), so non-cluster environments still work.
public sealed class BundleBuildOptions
{
    public const string SectionName = "BundleBuild";

    // Container image that runs generate -> build -> validate -> publish. Empty disables builds.
    public string BuilderImage { get; init; } = string.Empty;

    // Namespace the build Job is created in. Defaults to the Worker's own namespace at runtime.
    public string Namespace { get; init; } = "default";

    // Service account with workload identity + Storage Blob Data Contributor (writes the bundle).
    // Defaults to the Worker's own publisher identity so the build pod can upload the site tree.
    public string ServiceAccountName { get; init; } = "biller-publisher";

    // Generation mode passed to the builder: "deterministic" (offline) or "opus" (Claude Opus
    // on Azure AI Foundry).
    public string GeneratorMode { get; init; } = "deterministic";

    // Overall wall-clock budget for a single build (generate + vite build + Playwright + upload).
    public int JobTimeoutSeconds { get; init; } = 900;

    // Kubernetes-side deadline; the Job is failed if it runs past this.
    public int ActiveDeadlineSeconds { get; init; } = 1200;

    // How long a finished Job (and its pod/logs) is retained before automatic cleanup.
    public int TtlSecondsAfterFinished { get; init; } = 900;

    // Scratch volume size for staged builds and artifacts.
    public string WorkVolumeSizeLimit { get; init; } = "2Gi";

    // Non-root uid the builder image runs as (Playwright image's pwuser).
    public long RunAsUser { get; init; } = 1000;
}
