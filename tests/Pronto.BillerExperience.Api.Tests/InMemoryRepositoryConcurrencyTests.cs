using Pronto.BillerExperience.Api.Domain;
using Pronto.BillerExperience.Api.Infrastructure.Persistence;
using Pronto.BillerExperience.Contracts.V1.Billers;
using Pronto.BillerExperience.Contracts.V1.Experiences;
using Pronto.BillerExperience.Contracts.V1.Onboarding;
using Xunit;

namespace Pronto.BillerExperience.Api.Tests;

public sealed class InMemoryRepositoryConcurrencyTests
{
    [Fact]
    public async Task ConcurrentCreationsOnSameSlugElectExactlyOneWinner()
    {
        var repository = new InMemoryBillerExperienceRepository();
        var billers = Enumerable.Range(0, 32)
            .Select(index => Biller($"biller-{index}", "shared-slug"))
            .ToArray();

        var results = await Task.WhenAll(billers.Select(biller => Task.Run(async () =>
        {
            try
            {
                await repository.CreateBillerAsync(biller, CancellationToken.None);
                return true;
            }
            catch (SlugConflictException)
            {
                return false;
            }
        })));

        Assert.Equal(1, results.Count(won => won));
    }

    [Fact]
    public async Task ConcurrentExperienceWritesWithSameExpectedETagLoseExactlyOnce()
    {
        var repository = new InMemoryBillerExperienceRepository();
        var seeded = await repository.SaveExperienceAsync(Experience(1), null, CancellationToken.None);

        var results = await Task.WhenAll(
            Attempt(repository, Experience(2), seeded.ETag),
            Attempt(repository, Experience(3), seeded.ETag));

        Assert.Equal(1, results.Count(conflicted => conflicted));

        static Task<bool> Attempt(InMemoryBillerExperienceRepository repository, ExperienceRecord experience, string? etag) =>
            Task.Run(async () =>
            {
                try
                {
                    await repository.SaveExperienceAsync(experience, etag, CancellationToken.None);
                    return false;
                }
                catch (ConcurrencyException)
                {
                    return true;
                }
            });
    }

    [Fact]
    public async Task PurgeRemovesAgentActivityAndContext()
    {
        var repository = new InMemoryBillerExperienceRepository();
        await repository.CreateBillerAsync(Biller("biller-1", "biller-1-slug"), CancellationToken.None);
        await repository.SaveAgentContextAsync(
            new AgentContextRecord("context-run-1", "biller-1", "run-1", "agent_context", 0, "goal", [], DateTimeOffset.UtcNow),
            null,
            CancellationToken.None);
        await repository.AppendAgentActivityAsync(
            "biller-1",
            "run-1",
            new AgentActivityEvent("event-1", 1, "run-1", "research", "Research", AgentActivityStatus.Running, "Working", DateTimeOffset.UtcNow),
            CancellationToken.None);

        await repository.PurgeByBillerAsync("biller-1", CancellationToken.None);

        Assert.Null(await repository.GetAgentContextAsync("biller-1", "run-1", CancellationToken.None));
        Assert.Empty(await repository.GetAgentActivityAsync("biller-1", "run-1", CancellationToken.None));
        Assert.False(await repository.SlugExistsAsync("biller-1-slug", CancellationToken.None));
    }

    private static BillerRecord Biller(string id, string slug) => new(
        id,
        "Display",
        slug,
        "Utility",
        "75074",
        new Uri("https://example.com"),
        null,
        null,
        Array.Empty<PaymentRailReference>(),
        BillerStatus.Prospect,
        DateTimeOffset.UtcNow);

    private static ExperienceRecord Experience(int version) => new(
        "config-1",
        "biller-1",
        version,
        ExperienceRevisionState.Draft,
        null!,
        Array.Empty<ComplianceFinding>(),
        DateTimeOffset.UtcNow);
}
