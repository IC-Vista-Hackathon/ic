using Pronto.BillerExperience.Api.Application;
using Pronto.BillerExperience.Api.Application.Preview;
using Pronto.BillerExperience.Api.Infrastructure.AI;
using Pronto.BillerExperience.Api.Infrastructure.Persistence;
using Pronto.BillerExperience.Api.Infrastructure.SupportingServices;
using Pronto.BillerExperience.Contracts.V1.Billers;
using Pronto.ServiceDefaults;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Pronto.BillerExperience.Api.Tests;

public sealed class PreviewProvisioningServiceTests
{
    [Fact]
    public async Task ProvisionSeedsAnIsolatedPreviewTenantForTheBiller()
    {
        var seeder = new CapturingInvoiceSeeder();
        var (onboarding, service) = Create(seeder);
        var created = await onboarding.CreateAsync(CreateRequest(), CancellationToken.None);
        seeder.Reset(); // ignore the create-time seed of the live biller

        var descriptor = await service.ProvisionAsync(created.Biller.BillerId, CancellationToken.None);

        // The preview tenant is the live biller behind the preview- marker — an isolated partition.
        Assert.Equal(PreviewTenant.ForBiller(created.Biller.BillerId), descriptor.PreviewBillerId);
        Assert.True(PreviewTenant.IsPreview(descriptor.PreviewBillerId));
        Assert.Equal(created.Biller.BillerId, descriptor.BillerId);
        Assert.Equal("4421", descriptor.AccountNumber);
        Assert.Contains(descriptor.PreviewBillerId, descriptor.ConfigPath, StringComparison.Ordinal);

        // Seeding is delegated to the shared seeder, addressed to the preview tenant (not live).
        var seeded = Assert.Single(seeder.Contexts);
        Assert.Equal(descriptor.PreviewBillerId, seeded.BillerId);
        Assert.Equal("Utility", seeded.BillType);
        Assert.Equal(created.Biller.DisplayName, seeded.Name);
    }

    [Fact]
    public async Task ResetReseedsThePreviewTenantAgain()
    {
        var seeder = new CapturingInvoiceSeeder();
        var (onboarding, service) = Create(seeder);
        var created = await onboarding.CreateAsync(CreateRequest(), CancellationToken.None);
        seeder.Reset(); // ignore the create-time seed of the live biller

        await service.ProvisionAsync(created.Biller.BillerId, CancellationToken.None);
        await service.ResetAsync(created.Biller.BillerId, CancellationToken.None);

        // Both provision and reset run the (deterministic) seed against the same preview tenant.
        Assert.Equal(2, seeder.Contexts.Count);
        Assert.All(seeder.Contexts, context =>
            Assert.Equal(PreviewTenant.ForBiller(created.Biller.BillerId), context.BillerId));
        // Both run in replace mode so the preview partition is wiped + re-seeded, not accumulated.
        Assert.All(seeder.Replaced, replace => Assert.True(replace));
        Assert.Equal(2, seeder.Replaced.Count);
    }

    [Fact]
    public async Task PreviewConfigReflectsDraftScopedToThePreviewTenant()
    {
        var (onboarding, service) = Create(new CapturingInvoiceSeeder());
        var created = await onboarding.CreateAsync(CreateRequest(), CancellationToken.None);
        var previewBillerId = PreviewTenant.ForBiller(created.Biller.BillerId);

        var definition = await service.ResolvePreviewConfigAsync(previewBillerId, CancellationToken.None);

        // The built PWA loads the in-progress draft, but with biller_id pointed at the preview tenant
        // so its service calls hit the isolated, seeded partition.
        Assert.Equal(previewBillerId, definition.BillerId);
        Assert.Equal(created.Draft.Definition.Brand.DisplayName, definition.Brand.DisplayName);
    }

    [Fact]
    public async Task ProvisionUnknownBillerThrows()
    {
        var (_, service) = Create(new CapturingInvoiceSeeder());

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await service.ProvisionAsync("does-not-exist", CancellationToken.None));
    }

    private static (BillerOnboardingService Onboarding, PreviewProvisioningService Preview) Create(
        IInvoiceSeeder seeder)
    {
        var repository = new InMemoryBillerExperienceRepository();
        var generator = new DeterministicExperienceDraftGenerator(
            NullLogger<DeterministicExperienceDraftGenerator>.Instance);
        var onboarding = new BillerOnboardingService(
            repository,
            generator,
            new Pronto.Agentic.Orchestration.Execution.OrchestrationRunner(),
            NullLogger<BillerOnboardingService>.Instance,
            invoiceSeeder: seeder);
        var preview = new PreviewProvisioningService(
            onboarding, seeder, NullLogger<PreviewProvisioningService>.Instance);
        return (onboarding, preview);
    }

    private static CreateBillerRequest CreateRequest() =>
        new("City of Vista", "city-of-vista", "Utility", "02110", new Uri("https://vista.example"));

    private sealed class CapturingInvoiceSeeder : IInvoiceSeeder
    {
        public List<SeedBillerContext> Contexts { get; } = [];
        public List<bool> Replaced { get; } = [];

        public void Reset() { Contexts.Clear(); Replaced.Clear(); }

        public ValueTask SeedAsync(SeedBillerContext biller, CancellationToken cancellationToken, bool replace = false)
        {
            Contexts.Add(biller);
            Replaced.Add(replace);
            return ValueTask.CompletedTask;
        }
    }
}
