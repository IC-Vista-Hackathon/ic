using System.Net;
using System.Net.Http.Json;
using Pronto.ServiceDefaults.Security;
using Xunit;

namespace Pronto.BillerExperience.IntegrationTests;

/// <summary>
/// Auth/authz regression coverage for the Invoice host's internal endpoints: seed and status
/// transitions require their service roles, and tenant claims are validated against the route
/// biller.
/// </summary>
public sealed class InvoiceSecurityTests : IClassFixture<TestingAppFactory>
{
    private readonly TestingAppFactory _factory;

    public InvoiceSecurityTests(TestingAppFactory factory) => _factory = factory;

    [Fact]
    public async Task SeedRequiresInvoiceSeedRole()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.RolesHeader, ServiceClaims.PaymentServiceRole);

        var response = await client.PostAsJsonAsync(
            "/billers/biller-x/invoices/seed", new { count = 1 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task StatusTransitionRequiresPaymentServiceRole()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.RolesHeader, ServiceClaims.InvoiceSeedRole);

        var response = await client.PostAsJsonAsync(
            "/billers/biller-x/invoices/inv-1/status", new { status = "paid", payment_id = "p-1" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SeedRejectsMismatchedBillerClaim()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.RolesHeader, ServiceClaims.InvoiceSeedRole);
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.BillerHeader, "biller-other");

        var response = await client.PostAsJsonAsync(
            "/billers/biller-x/invoices/seed", new { count = 1 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(
            "biller_forbidden", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SeedAllowedForMatchingBillerClaim()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.RolesHeader, ServiceClaims.InvoiceSeedRole);
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.BillerHeader, "biller-scoped");

        var response = await client.PostAsJsonAsync(
            "/billers/biller-scoped/invoices/seed", new { count = 1 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
