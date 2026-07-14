using Xunit;

namespace Pronto.FunctionalTests;

[Trait("Category", "Functional")]
[Collection(FunctionalSuite.Name)]
public sealed class HealthFunctionalTests(DeployedEnvironment env)
{
    // Reachability through the gateway. Payment/PayerAccount are POST-only at their root,
    // so a GET returns 405 (the service answered) rather than exposing /health.
    [Theory]
    [InlineData("/invoices/health/ready", 200)]
    [InlineData("/api/health/ready", 200)]
    [InlineData("/payments/", 405)]
    [InlineData("/payers/", 405)]
    public async Task ServicesAreReachable(string path, int expectedStatus)
    {
        if (!env.Enabled)
        {
            return;
        }

        using var response = await env.Client.GetAsync(path);

        Assert.Equal(expectedStatus, (int)response.StatusCode);
    }
}
