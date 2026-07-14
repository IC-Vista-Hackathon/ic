using System.Diagnostics;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IC.BillerExperience.Api.Infrastructure.Publication;

public sealed partial class PublishedExperienceHealthCheck(
    BlobContainerClient container,
    ILogger<PublishedExperienceHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        using var activity = BillerExperienceTelemetry.Source.StartActivity("published_experience.health");
        try
        {
            var exists = await container.ExistsAsync(cancellationToken);
            if (exists.Value)
            {
                return HealthCheckResult.Healthy();
            }
            var exception = new InvalidOperationException($"Published-experience container '{container.Name}' does not exist.");
            activity?.SetStatus(ActivityStatusCode.Error, "container_missing");
            LogStorageHealthError(logger, container.Name, activity?.TraceId.ToString(), exception);
            return HealthCheckResult.Unhealthy(exception.Message, exception);
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            LogStorageHealthError(logger, container.Name, activity?.TraceId.ToString(), exception);
            return HealthCheckResult.Unhealthy("Published-experience storage is unavailable.", exception);
        }
    }

    [LoggerMessage(2400, LogLevel.Error, "Published-experience storage health check failed for container {ContainerName}; trace {TraceId}")]
    private static partial void LogStorageHealthError(ILogger logger, string containerName, string? traceId, Exception exception);
}
