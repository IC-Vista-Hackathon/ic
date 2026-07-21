using System.Diagnostics;
using Pronto.Agentic.Orchestration.Abstractions;
using Pronto.BillerExperience.Api.Application.Agents;
using Pronto.BillerExperience.Api.Application.Orchestration;
using Pronto.BillerExperience.Api.Application.PayerChat;

namespace Pronto.BillerExperience.Api.Application;

/// <summary>
/// Entry point for the payer-side pipeline. It assembles the inputs the reasoning stages need —
/// the biller's enabled payment methods and today's date — runs the <see cref="PayerChatWorkflow"/>
/// through the shared orchestration runner, and shapes the payer-facing reply. The workflow itself
/// owns the stage sequencing and telemetry.
/// </summary>
public sealed partial class PayerChatService(
    BillerOnboardingService onboarding,
    IBillIntelligenceAgent billIntelligence,
    IPaymentQuoteFetcher quoteFetcher,
    IFinancialPlanningAgent financialPlanning,
    PaymentPlanValidator validator,
    IOrchestrationRunner orchestrationRunner,
    TimeProvider timeProvider,
    ILogger<PayerChatService> logger)
{
    public async ValueTask<PayerChatResponse> ProcessTurnAsync(
        string billerId,
        PayerChatRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.InvoiceId))
        {
            throw new ArgumentException("invoice_id is required.", nameof(request));
        }

        var invoiceId = request.InvoiceId.Trim();

        // Enabled methods come from the biller's current experience config — the same source the
        // get_biller_configuration tool reads. GetDraftAsync throws KeyNotFound (→ 404) for an
        // unknown biller.
        var revision = await onboarding.GetDraftAsync(billerId, cancellationToken);
        var enabledMethods = revision.Definition.EnabledPaymentCapabilities;
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var context = OrchestrationContext.Create(
            billerId: billerId,
            correlationId: Activity.Current?.TraceId.ToString());
        var workflow = new PayerChatWorkflow(billIntelligence, quoteFetcher, financialPlanning, validator, logger);
        var result = await orchestrationRunner.RunAsync(
            workflow,
            new PayerChatWorkflowInput(invoiceId, enabledMethods, today, new NullOrchestrationEventSink()),
            context,
            cancellationToken);

        LogTurnCompleted(logger, billerId, result.Bill.InvoiceId, result.Plan.Method, result.Quotes.Count);
        var question = request.Messages?
            .LastOrDefault(message => string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase))?
            .Content;
        var reply = PayerChatResponder.Reply(question, result.Bill, result.Plan, result.Quotes);
        var action = PayerChatResponder.DetectAction(question, result.Plan);
        return new PayerChatResponse(
            reply,
            new PayerChatArtifacts(BillSummaryView.From(result.Bill), PaymentPlanView.From(result.Plan), action));
    }

    [LoggerMessage(3000, LogLevel.Information,
        "Payer-chat turn for biller {BillerId}, invoice {InvoiceId}: planned {Method} from {QuoteCount} quote(s)")]
    private static partial void LogTurnCompleted(ILogger logger, string billerId, string invoiceId, string method, int quoteCount);
}
