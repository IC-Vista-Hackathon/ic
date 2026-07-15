namespace Pronto.BillerExperience.Worker.Building;

// Used when no builder image is configured so local startup does not require Kubernetes.
// PublicationProcessor fails closed before publishing when this builder is disabled.
public sealed class NoOpBundleBuilder : IExperienceBundleBuilder
{
    public bool Enabled => false;

    public ValueTask BuildAsync(BundleBuildRequest request, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}
