using Azure.Storage.Blobs;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Pronto.PayerExperience.Router.Tests;

public sealed class RouterStorageHealthCheckTests
{
    [Fact]
    public async Task UnhealthyWhenStorageCannotBeReached()
    {
        var container = new BlobContainerClient(
            new Uri("https://127.0.0.1:1/payer-experiences"),
            new BlobClientOptions { Retry = { MaxRetries = 0 } });
        var check = new RouterStorageHealthCheck(container);

        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }
}
