using Microsoft.Extensions.Logging.Abstractions;
using Pronto.Agentic.Orchestration.Execution;
using Pronto.BillerExperience.Api.Application;
using Pronto.BillerExperience.Api.Infrastructure.AI;
using Pronto.BillerExperience.Api.Infrastructure.Persistence;
using Pronto.BillerExperience.Contracts.V1.Billers;
using Pronto.BillerExperience.Contracts.V1.Billing;
using Pronto.BillerExperience.Contracts.V1.Experiences;
using Pronto.BillerExperience.Contracts.V1.Onboarding;
using Xunit;

namespace Pronto.BillerExperience.Api.Tests;

public sealed class BillingDiscoveryEngineTests
{
    private readonly BillingDiscoveryEngine _engine = new(NullLogger<BillingDiscoveryEngine>.Instance);

    [Fact]
    public void DiegoScenarioRequiresPerPolicyCadenceRulesAndPaymentTerms()
    {
        var state = _engine.ApplyAnswer("diego", null,
            "We bill policy premiums by type (home, auto, life)").State;

        Assert.Equal(["Home Premium", "Auto Premium", "Life Premium"],
            state.Profile.Categories.Select(category => category.DisplayName));

        state = _engine.ApplyAnswer("diego", state.Profile,
            "Home premium is monthly, auto premium monthly, and life premium annually.").State;
        Assert.All(state.Profile.Categories, category => Assert.NotNull(category.Cadence));

        state = _engine.ApplyAnswer("diego", state.Profile,
            "Home policies lapse after a 30-day grace period.").State;
        state = _engine.ApplyAnswer("diego", state.Profile,
            "Auto policies lapse after a 15-day grace period.").State;
        state = _engine.ApplyAnswer("diego", state.Profile,
            "Life policies lapse after a 31-day grace period.").State;

        state = _engine.ApplyAnswer("diego", state.Profile,
            "Home, auto, and life premiums must all be paid in full; no installments.").State;
        Assert.All(state.Profile.Categories, category =>
            Assert.Equal(SettlementMode.PayInFull, category.PaymentTerms?.Mode));
        Assert.False(state.Progress.IsComplete);
        Assert.Equal(BillingDiscoveryDimension.Confirmation, state.CurrentQuestion?.Dimension);

        state = _engine.ApplyAnswer("diego", state.Profile, "Yes, that is correct.").State;
        Assert.True(state.Progress.IsComplete);
        Assert.True(state.Profile.Confirmed);
    }

    [Fact]
    public void RenataScenarioPreservesInstallmentContrastByCategory()
    {
        var state = _engine.ApplyAnswer("renata", null,
            "We bill people for dues, special assessment, and fines.").State;
        state = _engine.ApplyAnswer("renata", state.Profile,
            "Dues are quarterly, special assessment is one-time, and fines are ad hoc.").State;
        state = _engine.ApplyAnswer("renata", state.Profile,
            "Dues become delinquent after a 10-day grace period.").State;
        state = _engine.ApplyAnswer("renata", state.Profile,
            "No state change applies to the special assessment.").State;
        state = _engine.ApplyAnswer("renata", state.Profile,
            "Fines are late after the due date on each notice.").State;
        state = _engine.ApplyAnswer("renata", state.Profile,
            "The special assessment is splittable into up to 4 installments, but dues and fines are pay-in-full.").State;

        var assessment = Assert.Single(state.Profile.Categories, category => category.DisplayName == "Special Assessment");
        Assert.Equal(SettlementMode.InstallmentsAllowed, assessment.PaymentTerms?.Mode);
        Assert.Equal(4, assessment.PaymentTerms?.MaximumInstallments);
        Assert.Equal(SettlementMode.PayInFull,
            Assert.Single(state.Profile.Categories, category => category.DisplayName == "Dues").PaymentTerms?.Mode);
        Assert.Equal(SettlementMode.PayInFull,
            Assert.Single(state.Profile.Categories, category => category.DisplayName == "Fines").PaymentTerms?.Mode);
    }

    [Fact]
    public void UnrelatedDesignRequestCannotSkipRequiredQuestion()
    {
        var turn = _engine.ApplyAnswer("biller", null,
            "Build a blue insurance payment experience with a friendly heading.");

        Assert.False(turn.AnswerAccepted);
        Assert.Equal("billing.categories", turn.State.CurrentQuestion?.QuestionId);
        Assert.Empty(turn.State.Profile.Categories);
    }

    [Fact]
    public void AcceptedAnswersCreateBoundedGapSpecificFollowUps()
    {
        var profile = new BillingProfile("1.0",
        [
            new BillingCategory("assessment", "Assessment", new(BillingCadenceKind.OneTime),
                [new("Payment is late after a 10-day grace period.", 10)],
                new(SettlementMode.InstallmentsAllowed, Details: "It can be split."))
        ]);

        var state = _engine.Inspect(profile);
        Assert.Equal("state_transition_gap", state.CurrentQuestion?.ReasonCode);

        state = _engine.ApplyAnswer("biller", state.Profile, "It becomes delinquent.").State;
        Assert.Equal("missing_installment_limit", state.CurrentQuestion?.ReasonCode);

        state = _engine.ApplyAnswer("biller", state.Profile, "Up to 6 installments.").State;
        Assert.Equal(6, state.Profile.Categories[0].PaymentTerms?.MaximumInstallments);
        Assert.True(state.Profile.Categories[0].PaymentTerms?.LimitsConfirmed);
        Assert.Equal(BillingDiscoveryDimension.Confirmation, state.CurrentQuestion?.Dimension);
    }

    [Fact]
    public void FourBaseAnswersAreCapturedBeforeDerivedClarifications()
    {
        var state = _engine.ApplyAnswer("renata", null, "Dues and special assessment").State;
        state = _engine.ApplyAnswer("renata", state.Profile, "Dues quarterly and special assessment one-time").State;
        state = _engine.ApplyAnswer("renata", state.Profile,
            "Dues have a 10-day grace period, no state change for special assessment").State;

        Assert.Equal(BillingDiscoveryDimension.PaymentTerms, state.CurrentQuestion?.Dimension);

        state = _engine.ApplyAnswer("renata", state.Profile,
            "Special assessment allows up to 4 installments, dues are pay-in-full").State;

        Assert.All(state.Profile.Categories, category => Assert.NotNull(category.PaymentTerms));
        Assert.Equal("state_transition_gap", state.CurrentQuestion?.ReasonCode);
    }

    [Fact]
    public void ReopeningAnAnswerInvalidatesConfirmationAndDependentReadiness()
    {
        var profile = new BillingProfile("1.0",
        [
            new BillingCategory("dues", "Dues", new(BillingCadenceKind.Quarterly),
                [new("Delinquent after 10 days", 10, "delinquent")], new(SettlementMode.PayInFull), true)
        ], true);

        var state = _engine.Reopen("biller", profile, "billing.category.dues.payment_terms");

        Assert.False(state.Profile.Confirmed);
        Assert.Null(state.Profile.Categories[0].PaymentTerms);
        Assert.Equal("billing.category.dues.payment_terms", state.CurrentQuestion?.QuestionId);
    }

    [Fact]
    public async Task ProductionEnabledServiceBlocksApprovalUntilProfileIsConfirmed()
    {
        var repository = new InMemoryBillerExperienceRepository();
        var generator = new DeterministicExperienceDraftGenerator(NullLogger<DeterministicExperienceDraftGenerator>.Instance);
        var service = new BillerOnboardingService(repository, generator, new OrchestrationRunner(),
            NullLogger<BillerOnboardingService>.Instance, billingDiscovery: _engine);
        var created = await service.CreateAsync(
            new CreateBillerRequest("Association", "association", "Other", "10001"), CancellationToken.None);

        var blocked = await Assert.ThrowsAsync<ExperienceValidationException>(() => service.ApproveAsync(
            created.Biller.BillerId, new ApproveExperienceRequest(created.Draft.Revision, "tester"), CancellationToken.None).AsTask());

        Assert.Contains(blocked.Findings, finding => finding.Code == "BILLING_DISCOVERY_INCOMPLETE");
        Assert.Equal("billing.categories", created.Session.CurrentQuestion?.QuestionId);
    }

    [Fact]
    public async Task GuidedStudioAnswersAreAppliedInOrderInOneChatTurn()
    {
        var repository = new InMemoryBillerExperienceRepository();
        var generator = new DeterministicExperienceDraftGenerator(NullLogger<DeterministicExperienceDraftGenerator>.Instance);
        var service = new BillerOnboardingService(repository, generator, new OrchestrationRunner(),
            NullLogger<BillerOnboardingService>.Instance, billingDiscovery: _engine);
        var created = await service.CreateAsync(
            new CreateBillerRequest("Association", "guided-association", "Other", "10001"), CancellationToken.None);

        var response = await service.SendMessageAsync(created.Biller.BillerId, new SendOnboardingMessageRequest(
            "Build the requested association experience.",
            [
                new(BillingDiscoveryDimension.Categories, "Dues, special assessment, and fines."),
                new(BillingDiscoveryDimension.Cadence, "Dues are quarterly; special assessment is one-time; fines are ad hoc."),
                new(BillingDiscoveryDimension.StateRules, "Dues become delinquent after 10 days; no state change applies to special assessment; fines are late after each notice due date."),
                new(BillingDiscoveryDimension.PaymentTerms, "Special assessment allows up to 4 installments; dues and fines are pay-in-full.")
            ]), CancellationToken.None);

        Assert.Equal(3, response.Session.BillingProfile?.Categories.Count);
        Assert.All(response.Session.BillingProfile!.Categories, category => Assert.NotEmpty(category.StateRules!));
        var assessment = Assert.Single(response.Session.BillingProfile.Categories, category => category.DisplayName == "Special Assessment");
        Assert.Equal(SettlementMode.InstallmentsAllowed, assessment.PaymentTerms?.Mode);
        Assert.Equal(4, assessment.PaymentTerms?.MaximumInstallments);
        Assert.Equal(BillingDiscoveryDimension.Confirmation, response.Session.CurrentQuestion?.Dimension);
        var draft = Assert.IsType<ExperienceRevisionResponse>(response.Draft);
        Assert.Equal(3, draft.Definition.Billing?.Categories.Count);
        Assert.Equal("Up to 4 installments", DescribeTerms(
            Assert.Single(draft.Definition.Billing!.Categories, category => category.DisplayName == "Special Assessment")));
    }

    [Fact]
    public async Task GuidedStudioGenericAnswersFanOutAcrossMultipleCategories()
    {
        var repository = new InMemoryBillerExperienceRepository();
        var generator = new DeterministicExperienceDraftGenerator(NullLogger<DeterministicExperienceDraftGenerator>.Instance);
        var service = new BillerOnboardingService(repository, generator, new OrchestrationRunner(),
            NullLogger<BillerOnboardingService>.Instance, billingDiscovery: _engine);
        var created = await service.CreateAsync(
            new CreateBillerRequest("Acme Water Utility", "acme-water-utility", "Utilities", "85001"), CancellationToken.None);

        // The Studio collects one generic answer per dimension (no category named). With two
        // billing categories the server expands cadence/state_rules/payment_terms per category,
        // so the answer must fan out or the fixed four-answer batch desyncs and 400s.
        var response = await service.SendMessageAsync(created.Biller.BillerId, new SendOnboardingMessageRequest(
            "Build the requested utility experience.",
            [
                new(BillingDiscoveryDimension.Categories, "Water usage charges and sewer service fee."),
                new(BillingDiscoveryDimension.Cadence, "Everything is billed monthly."),
                new(BillingDiscoveryDimension.StateRules, "Accounts become delinquent after a 30-day grace period."),
                new(BillingDiscoveryDimension.PaymentTerms, "Everything must be paid in full.")
            ]), CancellationToken.None);

        Assert.Equal(2, response.Session.BillingProfile?.Categories.Count);
        Assert.All(response.Session.BillingProfile!.Categories, category =>
        {
            Assert.NotNull(category.Cadence);
            Assert.NotEmpty(category.StateRules!);
            Assert.Equal(SettlementMode.PayInFull, category.PaymentTerms?.Mode);
        });
        Assert.Equal(BillingDiscoveryDimension.Confirmation, response.Session.CurrentQuestion?.Dimension);
    }

    private static string DescribeTerms(BillingPresentationCategory category) =>
        category.PaymentMode == SettlementMode.InstallmentsAllowed
            ? $"Up to {category.MaximumInstallments} installments"
            : "Pay in full";
}
