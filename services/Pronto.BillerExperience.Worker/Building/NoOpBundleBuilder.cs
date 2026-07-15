namespace Pronto.BillerExperience.Worker.Building;

// Used when no builder image is configured (local dev, CI, config-only environments): the
// Worker keeps its prior behavior of publishing config/manifest without a static bundle.
public sealed class NoOpBundleBuilder : IExperienceBundleBuilder
{
    public bool Enabled => false;

    public ValueTask BuildAsync(BundleBuildRequest request, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}
