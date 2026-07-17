using Pronto.BillerExperience.Api.Application;
using Pronto.BillerExperience.Api.Application.Preview;
using Pronto.BillerExperience.Api.Controllers;
using Pronto.BillerExperience.Api.Infrastructure.AI;
using Pronto.BillerExperience.Api.Infrastructure.Persistence;
using Pronto.BillerExperience.Api.Infrastructure.SupportingServices;
using Pronto.BillerExperience.Contracts.V1.Billers;
using Pronto.BillerExperience.Contracts.V1.Experiences;
using Pronto.BillerExperience.Contracts.V1.Preview;
using Pronto.ServiceDefaults;
using Pronto.Agentic.Orchestration.Execution;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Pronto.BillerExperience.Api.Tests;

/// <summary>
/// Drives the Studio preview lifecycle end-to-end through the controller: provisioning seeds an
/// isolated preview tenant, the config endpoint serves the draft scoped to it, and reset re-seeds.
/// </summary>
public sealed class PreviewControllerTests
{
    [Fact]
    public async Task ProvisionThenGetConfigServesDraftScopedToPreviewTenant()
    {
        var (onboarding, controller, seeder) = Create();
        var billerId = await CreateBillerAsync(onboarding);

        var provision = await controller.Provision(billerId, CancellationToken.None);
        var descriptor = Assert.IsType<PreviewTenantResponse>(((OkObjectResult)provision.Result!).Value);
        Assert.Equal(PreviewTenant.ForBiller(billerId), descriptor.PreviewBillerId);
        // Provisioning seeded the isolated preview partition (not the live biller).
        Assert.Contains(seeder.Contexts, c => c.BillerId == descriptor.PreviewBillerId);

        var config = await controller.GetPreviewConfig(descriptor.PreviewBillerId, CancellationToken.None);
        var definition = Assert.IsType<BillerExperienceDefinition>(((OkObjectResult)config.Result!).Value);
        Assert.Equal(descriptor.PreviewBillerId, definition.BillerId);
        // The built PWA must never cache preview config across resets.
        Assert.Contains("no-store", controller.Response.Headers.CacheControl.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResetReseedsThePreviewTenant()
    {
        var (onboarding, controller, seeder) = Create();
        var billerId = await CreateBillerAsync(onboarding);
        await controller.Provision(billerId, CancellationToken.None);
        var previewSeedsAfterProvision = seeder.Contexts.Count(c => PreviewTenant.IsPreview(c.BillerId));

        var reset = await controller.Reset(billerId, CancellationToken.None);

        var descriptor = Assert.IsType<PreviewTenantResponse>(((OkObjectResult)reset.Result!).Value);
        Assert.Equal(PreviewTenant.ForBiller(billerId), descriptor.PreviewBillerId);
        Assert.Equal(
            previewSeedsAfterProvision + 1,
            seeder.Contexts.Count(c => PreviewTenant.IsPreview(c.BillerId)));
    }

    private static async Task<string> CreateBillerAsync(BillerOnboardingService onboarding)
    {
        var created = await onboarding.CreateAsync(
            new CreateBillerRequest("City of Vista", "city-of-vista", "Utility", "02110", new Uri("https://vista.example")),
            CancellationToken.None);
        return created.Biller.BillerId;
    }

    private static (BillerOnboardingService, PreviewController, CapturingInvoiceSeeder) Create()
    {
        var seeder = new CapturingInvoiceSeeder();
        var onboarding = new BillerOnboardingService(
            new InMemoryBillerExperienceRepository(),
            new DeterministicExperienceDraftGenerator(NullLogger<DeterministicExperienceDraftGenerator>.Instance),
            new OrchestrationRunner(),
            NullLogger<BillerOnboardingService>.Instance,
            invoiceSeeder: seeder);
        var preview = new PreviewProvisioningService(
            onboarding, NullLogger<PreviewProvisioningService>.Instance);
        var controller = new PreviewController(preview, NullLogger<PreviewController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        return (onboarding, controller, seeder);
    }

    private sealed class CapturingInvoiceSeeder : IInvoiceSeeder
    {
        public List<SeedBillerContext> Contexts { get; } = [];

        public ValueTask SeedAsync(SeedBillerContext biller, CancellationToken cancellationToken)
        {
            Contexts.Add(biller);
            return ValueTask.CompletedTask;
        }
    }
}
