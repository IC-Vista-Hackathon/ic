using Pronto.Agentic.Orchestration.Abstractions;
using Pronto.Agentic.Orchestration.Execution;
using Pronto.BillerExperience.Api.Application.Agents;
using Pronto.BillerExperience.Api.Domain;
using Pronto.BillerExperience.Api.Infrastructure;
using Pronto.Payment.Contracts.V1.Payments;

namespace Pronto.BillerExperience.Api.Application.Orchestration;

/// <summary>
/// One turn of the payer pipeline through the Financial Planning stage:
/// <c>Bill Intelligence → (quote) → Financial Planning → validate</c>. Bill Intelligence reads the
/// invoice; the orchestrator pre-fetches server quotes for the biller's enabled methods; Financial
/// Planning selects and explains a plan; a server-side gate confirms the plan's numbers match a
/// quote before it would move on to Policy. Policy and Execution are downstream stages, not built here.
/// </summary>
internal sealed record PayerChatWorkflowInput(
    string InvoiceId,
    IReadOnlyList<string> EnabledMethods,
    DateOnly Today,
    IOrchestrationEventSink EventSink);

/// <summary>The turn's artifacts: the bill, the plan, and the quotes the plan was validated against.</summary>
internal sealed record PayerPipelineResult(
    BillSummary Bill,
    PaymentPlan Plan,
    IReadOnlyList<PaymentQuoteResponse> Quotes);

internal sealed class PayerChatWorkflow(
    IBillIntelligenceAgent billIntelligence,
    IPaymentQuoteFetcher quoteFetcher,
    IFinancialPlanningAgent financialPlanning,
    PaymentPlanValidator validator,
    ILogger logger) : IOrchestrationWorkflow<PayerChatWorkflowInput, PayerPipelineResult>
{
    public const string WorkflowName = "payer-chat-turn";

    public string Name => WorkflowName;

    public async ValueTask<PayerPipelineResult> ExecuteAsync(
        PayerChatWorkflowInput input,
        OrchestrationContext context,
        CancellationToken cancellationToken = default)
    {
        var billerId = context.BillerId
            ?? throw new InvalidOperationException("A payer-chat turn requires a biller-scoped orchestration context.");

        var billStep = new ObservableOrchestrationStep<string, BillSummary>(
            "bill-intelligence", "Bill Intelligence", "Looking up the invoice and explaining what's owed",
            (invoiceId, _, token) => billIntelligence.SummarizeAsync(billerId, invoiceId, token),
            input.EventSink, logger);
        var bill = await billStep.ExecuteAsync(input.InvoiceId, context, cancellationToken);

        // Server-authoritative quotes, one per enabled method, injected into the planner. The planner
        // selects among these numbers; it never computes fees.
        var quotes = await quoteFetcher.FetchAsync(billerId, bill.InvoiceId, input.EnabledMethods, cancellationToken);

        var planStep = new ObservableOrchestrationStep<(BillSummary Bill, IReadOnlyList<PaymentQuoteResponse> Quotes), PaymentPlan>(
            "financial-planning", "Financial Planning", "Choosing the cheapest method and the best time to pay",
            (planInput, _, token) => financialPlanning.PlanAsync(planInput.Bill, planInput.Quotes, input.Today, token),
            input.EventSink, logger);
        var plan = await planStep.ExecuteAsync((bill, quotes), context, cancellationToken);

        // Trust the plan's choice, never its numbers: confirm method/fee/total/timing before Policy.
        var validation = validator.Validate(plan, quotes, bill);
        if (!validation.IsValid)
        {
            BillerExperienceTelemetry.PayerPlanRejected.Add(1, new KeyValuePair<string, object?>("code", validation.Code));
            throw new PayerPlanRejectedException(validation.Code!, validation.Reason!);
        }

        BillerExperienceTelemetry.PayerPlanProduced.Add(1, new KeyValuePair<string, object?>("method", plan.Method));
        return new PayerPipelineResult(bill, plan, quotes);
    }
}
