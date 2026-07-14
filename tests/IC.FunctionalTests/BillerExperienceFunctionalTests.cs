using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace IC.FunctionalTests;

[Trait("Category", "Functional")]
[Collection(FunctionalSuite.Name)]
public sealed class BillerExperienceFunctionalTests(DeployedEnvironment env)
{
    [Fact]
    public async Task CreateBillerThenReadConfiguration()
    {
        if (!env.Enabled)
        {
            return;
        }

        var slug = "func-" + Guid.NewGuid().ToString("N")[..10];

        using var createResponse = await env.Client.PostAsJsonAsync(
            "/api/billers",
            new CreateBiller("Functional Test Co", slug, "Utility", "75024"),
            env.Json);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var bootstrap = await createResponse.Content.ReadFromJsonAsync<Bootstrap>(env.Json);
        Assert.NotNull(bootstrap);
        var billerId = bootstrap!.Biller.BillerId;
        Assert.False(string.IsNullOrWhiteSpace(billerId));
        env.Track(billerId); // BillerExperience generates the id; ensure it is purged in teardown.

        using var getResponse = await env.Client.GetAsync($"/api/billers/{billerId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        using var configResponse = await env.Client.GetAsync($"/api/billers/{billerId}/config");
        Assert.Equal(HttpStatusCode.OK, configResponse.StatusCode);
    }

    private sealed record CreateBiller(string DisplayName, string Slug, string BillType, string PostalCode);

    private sealed record Bootstrap(BillerRef Biller);

    private sealed record BillerRef(string BillerId);
}
