using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Pronto.ServiceDefaults.Health;

/// <summary>
/// Health-check tags separating <b>liveness</b> (is the process up?) from <b>readiness</b>
/// (can it serve — are its dependencies reachable?). Kubernetes uses a failing liveness probe
/// to restart a pod and a failing readiness probe to pull it out of rotation, so conflating
/// them makes a transient dependency outage trigger pointless restarts.
/// </summary>
public static class HealthTags
{
    public const string Live = "live";
    public const string Ready = "ready";
}

/// <summary>
/// Wraps an async dependency probe as a readiness health check. A throwing or cancelled probe
/// reports <see cref="HealthStatus.Unhealthy"/> (or Degraded) so <c>/health/ready</c> fails
/// while <c>/health/live</c> stays green.
/// </summary>
public sealed class DependencyReadinessCheck : IHealthCheck
{
    private readonly string dependency;
    private readonly Func<IServiceProvider, CancellationToken, Task> probe;
    private readonly IServiceProvider services;

    public DependencyReadinessCheck(
        string dependency, Func<IServiceProvider, CancellationToken, Task> probe, IServiceProvider services)
    {
        this.dependency = dependency;
        this.probe = probe;
        this.services = services;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await probe(services, cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy($"{dependency} reachable.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new HealthCheckResult(
                context.Registration.FailureStatus, $"{dependency} not ready.", exception);
        }
    }
}

/// <summary>Registration helpers for Pronto readiness dependencies.</summary>
public static class HealthChecksExtensions
{
    /// <summary>
    /// Register a dependency probe under the <see cref="HealthTags.Ready"/> tag so it gates
    /// <c>/health/ready</c> but never <c>/health/live</c>. Hosts call this for each external
    /// dependency (e.g. Cosmos) they must reach before accepting traffic.
    /// </summary>
    public static IHealthChecksBuilder AddDependencyReadinessCheck(
        this IHealthChecksBuilder builder,
        string name,
        Func<IServiceProvider, CancellationToken, Task> probe)
    {
        builder.Add(new HealthCheckRegistration(
            name,
            services => new DependencyReadinessCheck(name, probe, services),
            HealthStatus.Unhealthy,
            [HealthTags.Ready]));
        return builder;
    }
}
