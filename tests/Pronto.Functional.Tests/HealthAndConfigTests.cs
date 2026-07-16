using System.Net;
using System.Text.Json.Nodes;
using Xunit;

namespace Pronto.Functional.Tests;

/// <summary>
/// Baseline reachability of the deployed control plane. These pass today and guard against a
/// broken deploy: a red result here means the environment itself is down, not a feature gap.
/// Covers FR-1 (Onboarding availability) and FR-9 (Browser telemetry config) — see
/// docs/pronto-functional-requirements.md.
/// </summary>
[Trait(Categories.Name, Categories.Functional)]
public sealed class HealthAndConfigTests
{
    [SkippableFact]
    public async Task ApiLivenessAndReadinessProbesReturnOk()
    {
        ProntoEnvironment.RequireBaseUrl();
        using var client = new ProntoApiClient();

        using var live = await client.GetAsync("health/live");
        using var ready = await client.GetAsync("health/ready");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    [SkippableFact]
    public async Task PublicTelemetryConfigExposesExpectedShape()
    {
        ProntoEnvironment.RequireBaseUrl();
        using var client = new ProntoApiClient();

        using var response = await client.GetAsync("public/telemetry");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        Assert.True(body.ContainsKey("connection_string"), "telemetry config must expose connection_string");
        Assert.True(body.ContainsKey("sampling_percentage"), "telemetry config must expose sampling_percentage");
    }
}
