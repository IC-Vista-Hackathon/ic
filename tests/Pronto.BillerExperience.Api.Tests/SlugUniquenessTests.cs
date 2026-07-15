using Pronto.BillerExperience.Api.Application;
using Pronto.BillerExperience.Api.Infrastructure.AI;
using Pronto.BillerExperience.Api.Infrastructure.Persistence;
using Pronto.BillerExperience.Contracts.V1.Billers;
using Pronto.Agentic.Orchestration.Execution;
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
    public async Task ConcurrentDuplicateSlugsRemainUnique()
    {
        var service = CreateService();

        var created = await Task.WhenAll(
            Enumerable.Range(0, 20)
                .Select(_ => service.CreateAsync(Request(), CancellationToken.None).AsTask()));

        Assert.Equal(20, created.Select(item => item.Biller.Slug).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(created, item => item.Biller.Slug == "city-of-plano");
    }

    [Fact]
    public async Task ExistingBillerWithoutReservationStillOwnsItsSlug()
    {
        var repository = new InMemoryBillerExperienceRepository();
        await repository.CreateBillerAsync(
            new(
                "legacy-biller",
                "Legacy City",
                "city-of-plano",
                "Utility",
                "75074",
                null,
                null,
                null,
                [],
                BillerStatus.Prospect,
                DateTimeOffset.UtcNow),
            CancellationToken.None);
        var service = CreateService(repository);

        var created = await service.CreateAsync(Request(), CancellationToken.None);

        Assert.Equal("city-of-plano-2", created.Biller.Slug);
    }

    [Fact]
    public async Task MixedCaseSlugIsNormalizedNotRejected()
    {
        var service = CreateService();

        var created = await service.CreateAsync(
            Request() with { Slug = " City-Of-Plano " }, CancellationToken.None);

        Assert.Equal("city-of-plano", created.Biller.Slug);
    }

    [Fact]
    public async Task MaxLengthSlugCollisionStaysWithin63Characters()
    {
        var service = CreateService();
        var maxLengthSlug = new string('a', 63);

        var first = await service.CreateAsync(
            Request() with { Slug = maxLengthSlug }, CancellationToken.None);
        var second = await service.CreateAsync(
            Request() with { Slug = maxLengthSlug }, CancellationToken.None);

        Assert.Equal(maxLengthSlug, first.Biller.Slug);
        Assert.Equal(new string('a', 61) + "-2", second.Biller.Slug);
    }

    [Fact]
    public async Task TruncationNeverLeavesDoubleHyphenBeforeSuffix()
    {
        var service = CreateService();
        var hyphenAtBoundary = new string('a', 60) + "-bc"; // 63 chars; cut lands on the hyphen

        await service.CreateAsync(Request() with { Slug = hyphenAtBoundary }, CancellationToken.None);
        var second = await service.CreateAsync(
            Request() with { Slug = hyphenAtBoundary }, CancellationToken.None);

        Assert.Equal(new string('a', 60) + "-2", second.Biller.Slug);
    }

    [Fact]
    public async Task UnrepairableSlugStillRejected()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(
            Request() with { Slug = "no spaces allowed!" }, CancellationToken.None).AsTask());
    }

    private static BillerOnboardingService CreateService(
        IBillerExperienceRepository? repository = null) => new(
        repository ?? new InMemoryBillerExperienceRepository(),
        new DeterministicExperienceDraftGenerator(NullLogger<DeterministicExperienceDraftGenerator>.Instance),
        new OrchestrationRunner(),
        NullLogger<BillerOnboardingService>.Instance,
        invoiceSeeder: null);

    private static CreateBillerRequest Request() =>
        new("City of Plano", "city-of-plano", "Utility", "75074", new Uri("https://plano.example"));
}
