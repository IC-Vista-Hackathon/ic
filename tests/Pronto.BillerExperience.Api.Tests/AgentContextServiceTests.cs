using Pronto.BillerExperience.Api.Application;
using Pronto.BillerExperience.Api.Configuration;
using Pronto.BillerExperience.Api.Domain;
using Pronto.BillerExperience.Api.Infrastructure.Mcp;
using Pronto.BillerExperience.Api.Infrastructure.Persistence;
using Pronto.BillerExperience.Contracts.V1.AgentContext;
using Pronto.BillerExperience.Contracts.V1.Onboarding;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Pronto.BillerExperience.Api.Tests;

public sealed class AgentContextServiceTests
{
    [Fact]
    public async Task ContextIsRunScopedVersionedAndRequiresCitationsForExternalLearning()
    {
        var repository = new InMemoryBillerExperienceRepository();
        await SeedRunAsync(repository);
        var service = new AgentContextService(repository, NullLogger<AgentContextService>.Instance);
        var initial = await service.EnsureAsync("biller-1", "run-1", "Build a safe experience", default);

        Assert.Equal(0, initial.Version);
        await Assert.ThrowsAsync<ArgumentException>(() => service.AppendAsync(
            "biller-1", "run-1",
            new AppendAgentContextRequest(0, AgentContextEntryKind.Observation, "research", "research", "External fact", [], true),
            default).AsTask());

        var updated = await service.AppendAsync(
            "biller-1", "run-1",
            new AppendAgentContextRequest(
                0, AgentContextEntryKind.Observation, "research", "research", "The public site identifies the utility.",
                [new Uri("https://example.com/about")], true),
            default);

        Assert.Equal(1, updated.Version);
        var entry = Assert.Single(updated.Entries);
        Assert.Equal("research", entry.AgentId);
        Assert.True(entry.External);
        await Assert.ThrowsAsync<ConcurrencyException>(() => service.AppendAsync(
            "biller-1", "run-1",
            new AppendAgentContextRequest(0, AgentContextEntryKind.Correction, "design", "brand", "Use the approved blue.", [], false),
            default).AsTask());
    }

    [Fact]
    public async Task ContextRejectsPaymentInstrumentData()
    {
        var repository = new InMemoryBillerExperienceRepository();
        await SeedRunAsync(repository);
        var service = new AgentContextService(repository, NullLogger<AgentContextService>.Instance);
        await service.EnsureAsync("biller-1", "run-1", "goal", default);

        await Assert.ThrowsAsync<ArgumentException>(() => service.AppendAsync(
            "biller-1", "run-1",
            new AppendAgentContextRequest(0, AgentContextEntryKind.Observation, "agent", "payment", "Card 4111 1111 1111 1111", [], false),
            default).AsTask());
    }

    [Fact]
    public void CapabilityIsAgentAndRunScopedTamperEvidentAndExpires()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 7, 14, 20, 0, 0, TimeSpan.Zero));
        var options = Options.Create(new BillerExperienceOptions
        {
            Mcp = new McpOptions
            {
                Enabled = true,
                ApiKey = new string('a', 32),
                CapabilitySigningKey = new string('s', 48),
                CapabilityLifetimeMinutes = 10
            }
        });
        var service = new AgentContextCapabilityService(options, time, NullLogger<AgentContextCapabilityService>.Instance);

        var token = service.Issue("biller-1", "run-1", "research-agent", canWrite: true);
        var scope = service.Validate(token, writeRequired: true);

        Assert.Equal("biller-1", scope.BillerId);
        Assert.Equal("run-1", scope.RunId);
        Assert.Equal("research-agent", scope.AgentId);
        Assert.Throws<UnauthorizedAccessException>(() => service.Validate(token + "x", writeRequired: false));
        time.Advance(TimeSpan.FromMinutes(11));
        Assert.Throws<UnauthorizedAccessException>(() => service.Validate(token, writeRequired: false));
    }

    private static async Task SeedRunAsync(InMemoryBillerExperienceRepository repository)
    {
        await repository.SaveRunAsync(new OnboardingRunRecord(
            "run-1", "biller-1", "test", OnboardingSessionState.CollectingInformation,
            0, [], [], DateTimeOffset.UtcNow), null, default);
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
