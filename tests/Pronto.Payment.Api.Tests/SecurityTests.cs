using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pronto.Payment.Api.Clients;
using Pronto.Payment.Contracts.V1.Payments;
using Pronto.ServiceDefaults.Health;
using Pronto.ServiceDefaults.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Pronto.Payment.Api.Tests;

/// <summary>
/// Regression coverage for the shared security/HTTP fixes: fail-closed production auth,
/// app-role policies, tenant (biller) claim validation, strict money-moving DTOs, and the
/// liveness/readiness split.
/// </summary>
public sealed class SecurityTests
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    [Fact]
    public void ProductionWithoutAuthorityFailsToStart()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

        // Fail closed: no identity provider configured → host refuses to start.
        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    [Fact]
    public async Task ProductionRejectsUnauthenticatedRequests()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting(
                    "Authentication:Authority", "https://login.microsoftonline.com/common/v2.0");
            });
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "payments", new CreatePaymentRequest("biller-1", "inv-1", "card"), Wire);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TestingEnvironmentAllowsFullAccessServicePrincipal()
    {
        var fakeInvoices = new FakeInvoiceClient();
        var biller = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(biller, amountCents: 5000);
        var client = CreateTestingClient(fakeInvoices);

        var response = await client.PostAsJsonAsync(
            "payments", new CreatePaymentRequest(biller, invoice.Id, "card"), Wire);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task WrongRoleIsForbiddenFromCreatingPayments()
    {
        var fakeInvoices = new FakeInvoiceClient();
        var biller = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(biller, amountCents: 5000);
        var client = CreateTestingClient(fakeInvoices);
        // A Policy Agent token must not be able to move money.
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.RolesHeader, ServiceClaims.PolicyAgentRole);

        var response = await client.PostAsJsonAsync(
            "payments", new CreatePaymentRequest(biller, invoice.Id, "card"), Wire);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ExecutionAgentScopedToOtherBillerIsForbidden()
    {
        var fakeInvoices = new FakeInvoiceClient();
        var biller = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(biller, amountCents: 5000);
        var client = CreateTestingClient(fakeInvoices);
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.RolesHeader, ServiceClaims.ExecutionAgentRole);
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.BillerHeader, "some-other-biller");

        var response = await client.PostAsJsonAsync(
            "payments", new CreatePaymentRequest(biller, invoice.Id, "card"), Wire);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(
            "biller_forbidden", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecutionAgentScopedToMatchingBillerSucceeds()
    {
        var fakeInvoices = new FakeInvoiceClient();
        var biller = Guid.NewGuid().ToString();
        var invoice = fakeInvoices.AddDueInvoice(biller, amountCents: 5000);
        var client = CreateTestingClient(fakeInvoices);
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.RolesHeader, ServiceClaims.ExecutionAgentRole);
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.BillerHeader, biller);

        var response = await client.PostAsJsonAsync(
            "payments", new CreatePaymentRequest(biller, invoice.Id, "card"), Wire);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task UnknownMemberOnPaymentRequestRejected()
    {
        var client = CreateTestingClient(new FakeInvoiceClient());
        using var body = new StringContent(
            """{"biller_id":"b","invoice_id":"i","method":"card","tip_cents":9999}""",
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync(new Uri("payments", UriKind.Relative), body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnknownMemberOnPurchaseRequestRejected()
    {
        var client = CreateTestingClient(new FakeInvoiceClient());
        using var body = new StringContent(
            """{"biller_id":"b","plan":"isolated","coupon":"FREE"}""",
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync(new Uri("purchases", UriKind.Relative), body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task LivenessIgnoresDependencyFailureWhileReadinessReflectsIt()
    {
        using var factory = new TestingAppFactory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddHealthChecks().AddDependencyReadinessCheck(
                    "boom", (_, _) => throw new InvalidOperationException("dependency down"))));
        var client = factory.CreateClient();

        var live = await client.GetAsync(new Uri("/health/live", UriKind.Relative));
        var ready = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
    }

    [Fact]
    public async Task HealthProbesAreAnonymousInProduction()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting(
                    "Authentication:Authority", "https://login.microsoftonline.com/common/v2.0");
            });
        var client = factory.CreateClient();

        var live = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    private static HttpClient CreateTestingClient(FakeInvoiceClient fakeInvoices) =>
        new TestingAppFactory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.Replace(ServiceDescriptor.Singleton<IInvoiceClient>(fakeInvoices))))
            .CreateClient();
}
