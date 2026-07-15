using Pronto.BillerExperience.Api.Domain;
using Pronto.Payment.Contracts.V1.Payments;

namespace Pronto.BillerExperience.Api.Application.Agents;

/// <summary>Outcome of validating a <see cref="PaymentPlan"/> against the quotes it was planned from.</summary>
public sealed record PlanValidation(bool IsValid, string? Code = null, string? Reason = null)
{
    public static readonly PlanValidation Valid = new(true);
    public static PlanValidation Rejected(string code, string reason) => new(false, code, reason);
}

/// <summary>
/// Server-side gate between Financial Planning and Policy. Financial Planning is trusted to
/// <em>choose</em> a method and timing, but never to invent numbers: the plan's method must be one
/// that was quoted, its fee/total must equal that quote exactly, and it must not schedule past the
/// due date. The deterministic planner always passes; this exists to catch a future model planner
/// that drifts. See design/services.md (Financial Planning → Policy).
/// </summary>
public sealed class PaymentPlanValidator
{
    public PlanValidation Validate(PaymentPlan plan, IReadOnlyList<PaymentQuoteResponse> quotes, BillSummary bill)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(quotes);
        ArgumentNullException.ThrowIfNull(bill);

        var quote = quotes.FirstOrDefault(q => string.Equals(q.Method, plan.Method, StringComparison.Ordinal));
        if (quote is null)
        {
            return PlanValidation.Rejected("method_not_quoted",
                $"Plan chose '{plan.Method}', which was not among the quoted methods.");
        }

        if (quote.FeeCents != plan.FeeCents)
        {
            return PlanValidation.Rejected("fee_mismatch",
                $"Plan fee {plan.FeeCents}¢ does not match the quoted fee {quote.FeeCents}¢ for '{plan.Method}'.");
        }

        if (quote.TotalCents != plan.TotalCents)
        {
            return PlanValidation.Rejected("total_mismatch",
                $"Plan total {plan.TotalCents}¢ does not match the quoted total {quote.TotalCents}¢ for '{plan.Method}'.");
        }

        if (plan.ScheduledFor is { } scheduledFor && scheduledFor > bill.DueDate)
        {
            return PlanValidation.Rejected("scheduled_past_due",
                $"Plan schedules {scheduledFor:yyyy-MM-dd}, later than the due date {bill.DueDate:yyyy-MM-dd}.");
        }

        return PlanValidation.Valid;
    }
}

/// <summary>
/// Thrown when a produced plan fails <see cref="PaymentPlanValidator"/> — an internal inconsistency
/// (the numbers the payer would approve would not match what the Payment Service charges), so the
/// turn is refused rather than forwarded to Policy.
/// </summary>
public sealed class PayerPlanRejectedException(string code, string reason)
    : InvalidOperationException(reason)
{
    public string Code { get; } = code;
}
