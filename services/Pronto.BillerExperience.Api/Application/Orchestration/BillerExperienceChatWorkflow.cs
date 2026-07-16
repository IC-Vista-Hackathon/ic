using System.Diagnostics;
using Pronto.Agentic.Orchestration.Abstractions;
using Pronto.Agentic.Orchestration.Execution;
using Pronto.BillerExperience.Api.Application.Agents;
using Pronto.BillerExperience.Api.Domain;
using Pronto.BillerExperience.Api.Infrastructure.AI;
using Pronto.BillerExperience.Api.Infrastructure.Research;
using Pronto.BillerExperience.Contracts.V1.Experiences;
using Pronto.BillerExperience.Contracts.V1.Billing;
using Pronto.BillerExperience.Contracts.V1.Onboarding;
using Pronto.BillerExperience.Contracts.V1.Research;

namespace Pronto.BillerExperience.Api.Application.Orchestration;

internal sealed record BillerExperienceChatWorkflowInput(
    BillerRecord Biller,
    ExperienceRecord Experience,
    IReadOnlyList<OnboardingChatMessage> Messages,
    BillingProfile BillingProfile,
    IOrchestrationEventSink EventSink);

internal sealed partial class BillerExperienceChatWorkflow(
    IExperienceDesignAgent designAgent,
    IAccessibilityReviewAgent accessibilityAgent,
    IComplianceReviewAgent complianceAgent,
    IBillerResearchCoordinator? researchCoordinator,
    ILogger logger,
    bool researchRequired) : IOrchestrationWorkflow<BillerExperienceChatWorkflowInput, DraftGenerationResult>
{
    public const string WorkflowName = "biller-experience-chat-turn";

    public string Name => WorkflowName;

    public async ValueTask<DraftGenerationResult> ExecuteAsync(
        BillerExperienceChatWorkflowInput input,
        OrchestrationContext context,
        CancellationToken cancellationToken = default)
    {
        var researchStep = new ObservableOrchestrationStep<BillerRecord, BillerResearchResponse>(
            "research-orchestration", "Research Orchestration", "Reviewing the supplied biller profile and brand context",
            (biller, stepContext, token) => ResearchAsync(biller, input.EventSink, stepContext, token),
            input.EventSink,
            logger,
            research => research.Outcome switch
            {
                ResearchOutcome.Skipped => (OrchestrationEventStatus.Skipped,
                    DescribeSkippedResearch(research.ErrorCode), research.ErrorCode),
                ResearchOutcome.Degraded => (OrchestrationEventStatus.Degraded,
                    "Research completed with warnings.", research.ErrorCode),
                ResearchOutcome.Failed when !researchRequired => (OrchestrationEventStatus.Degraded,
                    "Research was unavailable; continuing with supplied biller information.", research.ErrorCode),
                ResearchOutcome.Failed => (OrchestrationEventStatus.Failed, "Research failed.", research.ErrorCode),
                _ => (OrchestrationEventStatus.Completed, "Research completed successfully.", null)
            });
        var research = await researchStep.ExecuteAsync(input.Biller, context, cancellationToken);

        if (research.Outcome == ResearchOutcome.Failed)
        {
            LogResearchFailure(logger, input.Biller.Id, research.ErrorCode ?? "research.failed", research.Retryable,
                Activity.Current?.TraceId.ToString());
            if (researchRequired)
            {
                throw new InvalidOperationException($"Required biller research failed: {research.ErrorCode ?? "research.failed"}.");
            }

            research = research with
            {
                Outcome = ResearchOutcome.Degraded,
                Warnings = research.Warnings.Append(research.ErrorCode ?? "research.failed")
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()
            };
        }

        var designStep = new ObservableOrchestrationStep<ExperienceRecord, DraftGenerationResult>(
            "experience-designer", "Experience Designer", "Applying copy, layout, and action changes to the live preview",
            (experience, _, token) => designAgent.DesignAsync(input.Biller, experience, input.Messages, input.BillingProfile, research, token),
            input.EventSink, logger);
        var generated = await designStep.ExecuteAsync(input.Experience, context, cancellationToken);

        var accessibilityStep = new ObservableOrchestrationStep<DraftGenerationResult, IReadOnlyList<ComplianceFinding>>(
            "accessibility", "Accessibility", "Checking colors, hierarchy, and action clarity",
            (result, _, token) => accessibilityAgent.ReviewAsync(result.Definition, token), input.EventSink, logger);
        var complianceStep = new ObservableOrchestrationStep<DraftGenerationResult, IReadOnlyList<ComplianceFinding>>(
            "compliance", "Compliance", "Checking payment capabilities and required review guidance",
            (result, _, token) => complianceAgent.ReviewAsync(input.Biller, result.Definition, token),
            input.EventSink, logger);
        var accessibilityTask = accessibilityStep.ExecuteAsync(generated, context, cancellationToken).AsTask();
        var complianceTask = complianceStep.ExecuteAsync(generated, context, cancellationToken).AsTask();
        await Task.WhenAll(accessibilityTask, complianceTask);

        var accessibilityFindings = await accessibilityTask;
        var complianceFindings = await complianceTask;
        return generated with
        {
            Findings = generated.Findings.Concat(accessibilityFindings).Concat(complianceFindings)
                .GroupBy(finding => finding.Code, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray()
        };
    }

    // A skipped research outcome has two very different meanings: no provider was configured/eligible
    // at all, versus a provider that ran but surfaced nothing citable. Reporting the latter as
    // "no research provider was available" reads as a broken agent, so keep the message honest.
    private static string DescribeSkippedResearch(string? errorCode) => errorCode switch
    {
        "research.not_configured" or "research.no_eligible_agents" =>
            "Research was skipped because no research provider was available.",
        _ => "Research ran but found no citable public information; using the supplied biller details.",
    };

    private ValueTask<BillerResearchResponse> ResearchAsync(
        BillerRecord biller,
        IOrchestrationEventSink eventSink,
        OrchestrationContext context,
        CancellationToken cancellationToken)
    {
        if (researchCoordinator is null)
        {
            return ValueTask.FromResult(new BillerResearchResponse(
                ResearchOutcome.Skipped, [], [], ["research.not_configured"], "research.not_configured"));
        }

        return new ValueTask<BillerResearchResponse>(researchCoordinator.ResearchAsync(
            new BillerResearchRequest(
                biller.Website,
                "Research the biller brand, services, and customer-facing payment context.",
                BillerName: biller.Name,
                BillType: biller.BillType,
                PostalCode: biller.PostalCode),
            new ResearchExecutionContext(biller.Id, context.RunId, eventSink),
            cancellationToken));
    }

    [LoggerMessage(1950, LogLevel.Error,
        "Optional biller research failed for biller {BillerId} with {ErrorCode}; retryable {Retryable}, trace {TraceId}; continuing degraded")]
    private static partial void LogResearchFailure(
        ILogger logger,
        string billerId,
        string errorCode,
        bool retryable,
        string? traceId);
}
