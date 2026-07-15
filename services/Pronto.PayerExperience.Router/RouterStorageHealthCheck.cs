using Azure;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Pronto.PayerExperience.Router;

public sealed class RouterStorageHealthCheck(BlobContainerClient container) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var exists = await container.ExistsAsync(cancellationToken);
            return exists.Value
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"Blob container '{container.Name}' does not exist.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (RequestFailedException exception)
        {
            return HealthCheckResult.Unhealthy("Payer experience storage is unavailable.", exception);
        }
    }
}
