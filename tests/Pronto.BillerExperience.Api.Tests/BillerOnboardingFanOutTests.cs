using Microsoft.Extensions.Logging.Abstractions;
using Pronto.Agentic.Orchestration.Execution;
using Pronto.BillerExperience.Api.Application;
using Pronto.BillerExperience.Api.Infrastructure.AI;
using Pronto.BillerExperience.Api.Infrastructure.Persistence;
using Pronto.BillerExperience.Contracts.V1.Billers;
using Pronto.BillerExperience.Contracts.V1.Billing;
using Pronto.BillerExperience.Contracts.V1.Onboarding;
using Xunit;

namespace Pronto.BillerExperience.Api.Tests;

public sealed class BillerOnboardingFanOutTests
{
    // Reports every answer as accepted while leaving the profile — and therefore the current
    // question — unchanged. That is the "non-idempotent accept that never advances" which, before
    // the forward-progress guard in the guided fan-out loop, spun ApplyAnswer indefinitely and
    // flooded telemetry. Delegates the rest to a real engine so the surrounding flow is realistic.
    private sealed class NonAdvancingDiscovery(BillingDiscoveryEngine inner) : IBillingDiscovery
    {
        // A regressed fan-out spins forever; the synchronous loop can't be aborted by the test
        // timeout, so bound the pathology here and fail fast and clearly instead of hanging CI.
        private const int RunawayThreshold = 100;

        public int ApplyAnswerCalls { get; private set; }

        public BillingDiscoveryState Inspect(BillingProfile? source) => inner.Inspect(source);

        public BillingDiscoveryAssumptionTurn ApplyAssumptions(string billerId, BillingProfile? source, string? billType) =>
            inner.ApplyAssumptions(billerId, source, billType);

        public BillingDiscoveryState Reopen(string billerId, BillingProfile? source, string questionId) =>
            inner.Reopen(billerId, source, questionId);

        public BillingDiscoveryTurn ApplyAnswer(string billerId, BillingProfile? source, string message)
        {
            ApplyAnswerCalls++;
            if (ApplyAnswerCalls > RunawayThreshold)
            {
                throw new InvalidOperationException(
                    $"Guided fan-out re-applied a non-advancing answer {ApplyAnswerCalls} times without stopping.");
            }
            return new BillingDiscoveryTurn(inner.Inspect(source), AnswerAccepted: true, "accepted (no progress)");
        }
    }

    [Fact(Timeout = 30000)]
    public async Task GuidedFanOutTerminatesWhenAnswersNeverAdvanceTheQuestion()
    {
        var repository = new InMemoryBillerExperienceRepository();
        var generator = new DeterministicExperienceDraftGenerator(NullLogger<DeterministicExperienceDraftGenerator>.Instance);
        var discovery = new NonAdvancingDiscovery(new BillingDiscoveryEngine(NullLogger<BillingDiscoveryEngine>.Instance));
        var service = new BillerOnboardingService(repository, generator, new OrchestrationRunner(),
            NullLogger<BillerOnboardingService>.Instance, billingDiscovery: discovery);
        var created = await service.CreateAsync(
            new CreateBillerRequest("Acme Water Utility", "acme-water-utility", "Utilities", "85001"), CancellationToken.None);

        // A guided Categories answer that the engine "accepts" without advancing the current
        // question. Before the guard this looped forever; now the fan-out must stop after it sees
        // the question fail to advance, so the call returns and ApplyAnswer runs a bounded number
        // of times (the initial apply + one sibling that detects no progress).
        var response = await service.SendMessageAsync(created.Biller.BillerId, new SendOnboardingMessageRequest(
            "Build the requested utility experience.",
            [new(BillingDiscoveryDimension.Categories, "Water usage charges and sewer service fee.")]),
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.True(discovery.ApplyAnswerCalls <= 2,
            $"fan-out did not stop on a non-advancing answer (ApplyAnswer called {discovery.ApplyAnswerCalls} times)");
    }
}
