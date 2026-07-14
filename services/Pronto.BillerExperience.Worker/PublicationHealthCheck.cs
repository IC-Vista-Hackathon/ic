using System.Diagnostics;
using Azure.Storage.Blobs;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Pronto.BillerExperience.Worker;

public sealed partial class PublicationHealthCheck(
    CosmosClient cosmos,
    BlobContainerClient container,
    ILogger<PublicationHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        using var activity = PublicationTelemetry.Source.StartActivity("publication.dependencies.health");
        try
        {
            await cosmos.ReadAccountAsync();
            var exists = await container.ExistsAsync(cancellationToken);
            if (!exists.Value)
            {
                var exception = new InvalidOperationException($"Blob container '{container.Name}' does not exist.");
                activity?.SetStatus(ActivityStatusCode.Error, "container_missing");
                LogDependencyHealthError(logger, container.Name, activity?.TraceId.ToString(), exception);
                return HealthCheckResult.Unhealthy(exception.Message, exception);
            }
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            LogDependencyHealthError(logger, container.Name, activity?.TraceId.ToString(), exception);
            return HealthCheckResult.Unhealthy("Publication dependencies are unavailable.", exception);
        }
    }

    [LoggerMessage(1904, LogLevel.Error, "Publication dependency health check failed for container {ContainerName}; trace {TraceId}")]
    private static partial void LogDependencyHealthError(ILogger logger, string containerName, string? traceId, Exception exception);
}
