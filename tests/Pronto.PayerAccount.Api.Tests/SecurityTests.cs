using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pronto.PayerAccount.Contracts.V1.Payers;
using Pronto.ServiceDefaults.Security;
using Xunit;

namespace Pronto.PayerAccount.Api.Tests;

/// <summary>
/// Auth/authz regression coverage for the Payer Account host: payer writes require the Policy
/// Agent role, and tenant claims are validated against the request biller.
/// </summary>
public sealed class SecurityTests : IClassFixture<TestingAppFactory>
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private readonly TestingAppFactory _factory;

    public SecurityTests(TestingAppFactory factory) => _factory = factory;

    [Fact]
    public async Task RegisterRequiresPolicyAgentRole()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.RolesHeader, ServiceClaims.ExecutionAgentRole);

        var response = await client.PostAsJsonAsync(
            "payers",
            new RegisterPayerRequest(Guid.NewGuid().ToString(), "N", "n@example.com", null, []),
            Wire);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RegisterRejectsMismatchedBillerClaim()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.RolesHeader, ServiceClaims.PolicyAgentRole);
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.BillerHeader, "biller-other");

        var response = await client.PostAsJsonAsync(
            "payers",
            new RegisterPayerRequest("biller-x", "N", "n@example.com", null, []),
            Wire);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(
            "biller_forbidden", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RegisterAllowedForMatchingBillerClaim()
    {
        var client = _factory.CreateClient();
        var biller = Guid.NewGuid().ToString();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.RolesHeader, ServiceClaims.PolicyAgentRole);
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.BillerHeader, biller);

        var response = await client.PostAsJsonAsync(
            "payers",
            new RegisterPayerRequest(biller, "N", $"{Guid.NewGuid()}@example.com", null, []),
            Wire);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
