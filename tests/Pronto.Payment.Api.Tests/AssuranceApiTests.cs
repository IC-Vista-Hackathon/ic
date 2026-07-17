using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pronto.Payment.Api.Assurance;
using Pronto.ServiceDefaults.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Pronto.Payment.Api.Tests;

public sealed class AssuranceApiTests : IClassFixture<TestingAppFactory>
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private readonly TestingAppFactory factory;

    public AssuranceApiTests(TestingAppFactory factory) => this.factory = factory;

    [Fact]
    public async Task ReconcileReturns200OnCleanLedger()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "internal/assurance/reconcile", new ReconciliationRequest(), Wire);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ReconciliationResult>(Wire);
        Assert.True(result!.Ok);
    }

    [Fact]
    public async Task ReconcileReturns409OnDivergence()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "internal/assurance/reconcile",
            new ReconciliationRequest(["PRONTO-GHOST0"]),
            Wire);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ReconciliationResult>(Wire);
        Assert.False(result!.Ok);
        Assert.Contains(
            result.Findings, f => f.Code == ReconciliationFindingCodes.ConfirmationWithoutRecord);
    }

    [Fact]
    public async Task ReconcileRequiresMaintenanceRole()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            TestAuthenticationHandler.RolesHeader, ServiceClaims.ExecutionAgentRole);

        var response = await client.PostAsJsonAsync(
            "internal/assurance/reconcile", new ReconciliationRequest(), Wire);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CanaryReturns200WhenNoTargetsConfigured()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsync(new Uri("internal/assurance/canary", UriKind.Relative), null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<CanaryRunResult>(Wire);
        Assert.True(result!.Ok);
        Assert.Equal(0, result.TargetCount);
    }
}
