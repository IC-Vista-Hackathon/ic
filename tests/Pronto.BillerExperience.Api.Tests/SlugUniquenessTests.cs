using Pronto.BillerExperience.Api.Application;
using Pronto.BillerExperience.Api.Infrastructure.AI;
using Pronto.BillerExperience.Api.Infrastructure.Persistence;
using Pronto.BillerExperience.Contracts.V1.Billers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Pronto.BillerExperience.Api.Tests;

/// <summary>
/// Published artifacts and public reads are keyed by slug — two billers sharing one would
/// overwrite each other's payer experience.
/// </summary>
public sealed class SlugUniquenessTests
{
    [Fact]
    public async Task DuplicateSlugsGetUniqueSuffixes()
    {
        var service = CreateService();

        var first = await service.CreateAsync(Request(), CancellationToken.None);
        var second = await service.CreateAsync(Request(), CancellationToken.None);
        var third = await service.CreateAsync(Request(), CancellationToken.None);

        Assert.Equal("city-of-plano", first.Biller.Slug);
        Assert.Equal("city-of-plano-2", second.Biller.Slug);
        Assert.Equal("city-of-plano-3", third.Biller.Slug);
    }

    [Fact]
    public async Task MixedCaseSlugIsRejectedByValidation()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(
            Request() with { Slug = "City-Of-Plano" }, CancellationToken.None).AsTask());
    }

    private static BillerOnboardingService CreateService() => new(
        new InMemoryBillerExperienceRepository(),
        new DeterministicExperienceDraftGenerator(NullLogger<DeterministicExperienceDraftGenerator>.Instance),
        NullLogger<BillerOnboardingService>.Instance,
        invoiceSeeder: null);

    private static CreateBillerRequest Request() =>
        new("City of Plano", "city-of-plano", "Utility", "75074", new Uri("https://plano.example"));
}
