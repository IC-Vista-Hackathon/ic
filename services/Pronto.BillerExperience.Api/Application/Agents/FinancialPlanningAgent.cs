using System.Globalization;
using Pronto.BillerExperience.Api.Domain;
using Pronto.Payment.Contracts.V1.Payments;

namespace Pronto.BillerExperience.Api.Application.Agents;

/// <summary>
/// The Financial Planning stage of the payer pipeline. It turns a <see cref="BillSummary"/> and the
/// server-computed quotes for the biller's enabled methods into a concrete <see cref="PaymentPlan"/>
/// (method + timing). It is a reasoning stage: it <em>selects</em> among quotes and explains the
/// choice — it never computes fees and never moves money. See agents/financial-planning/instructions.md.
/// </summary>
public interface IFinancialPlanningAgent
{
    /// <param name="quotes">
    /// Pre-fetched, server-authoritative quotes — one per enabled method — for <paramref name="bill"/>'s
    /// invoice. The plan's fee/total are copied from the selected quote, so what is planned equals what
    /// the Payment Service will charge.
    /// </param>
    /// <param name="today">Injected current date — pay-now vs. schedule must not depend on a model clock.</param>
    ValueTask<PaymentPlan> PlanAsync(
        BillSummary bill,
        IReadOnlyList<PaymentQuoteResponse> quotes,
        DateOnly today,
        CancellationToken cancellationToken);
}

/// <summary>
/// Deterministic planner — the demo's default and the fallback when Foundry is unavailable (same role
/// the deterministic draft generator plays on the biller side). Rules:
/// <list type="bullet">
///   <item>Method: the quote with the lowest <c>total_cents</c> (cheapest for the payer).</item>
///   <item>Timing: schedule on the due date if it is more than <see cref="ScheduleThresholdDays"/> days
///   out — keeping the payer's money until it is due — otherwise pay now.</item>
/// </list>
/// It reads fees only from the supplied quotes; it performs no fee arithmetic.
/// </summary>
public sealed class DeterministicFinancialPlanningAgent : IFinancialPlanningAgent
{
    /// <summary>A due date more than this many days out is worth scheduling for rather than paying now.</summary>
    private const int ScheduleThresholdDays = 3;

    public ValueTask<PaymentPlan> PlanAsync(
        BillSummary bill,
        IReadOnlyList<PaymentQuoteResponse> quotes,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bill);
        ArgumentNullException.ThrowIfNull(quotes);
        cancellationToken.ThrowIfCancellationRequested();

        if (quotes.Count == 0)
        {
            throw new InvalidOperationException(
                $"Cannot plan a payment for invoice {bill.InvoiceId}: no payment methods are quoted.");
        }

        // Cheapest total for the payer; deterministic tie-break so equal totals always resolve the same way.
        var chosen = quotes
            .OrderBy(quote => quote.TotalCents)
            .ThenBy(quote => quote.FeeCents)
            .ThenBy(quote => quote.Method, StringComparer.Ordinal)
            .First();

        var daysUntilDue = bill.DueDate.DayNumber - today.DayNumber;
        var schedule = daysUntilDue > ScheduleThresholdDays;
        var scheduledFor = schedule ? bill.DueDate : (DateOnly?)null;

        var rationale = BuildRationale(bill, quotes, chosen, scheduledFor, daysUntilDue);

        return ValueTask.FromResult(new PaymentPlan(
            chosen.Method,
            scheduledFor,
            chosen.FeeCents,
            chosen.TotalCents,
            rationale));
    }

    private static string BuildRationale(
        BillSummary bill,
        IReadOnlyList<PaymentQuoteResponse> quotes,
        PaymentQuoteResponse chosen,
        DateOnly? scheduledFor,
        int daysUntilDue)
    {
        var method = MethodLabel(chosen.Method);
        var amount = Money(bill.AmountCents);

        string methodReason;
        var cheapestAlternative = quotes
            .Where(quote => !ReferenceEquals(quote, chosen) && quote.TotalCents > chosen.TotalCents)
            .OrderBy(quote => quote.TotalCents)
            .FirstOrDefault();

        if (chosen.TotalCents == chosen.AmountCents && chosen.FeeCents > 0)
        {
            // payer_pays_fee is off: the fee is displayed but the biller absorbs it.
            methodReason = $"{method} carries a {Money(chosen.FeeCents)} fee that the biller absorbs, so you pay just {amount} on this bill.";
        }
        else if (cheapestAlternative is not null)
        {
            methodReason = $"{method}'s {Money(chosen.FeeCents)} fee beats {MethodLabel(cheapestAlternative.Method)}'s "
                + $"{Money(cheapestAlternative.FeeCents)} on this {amount} bill.";
        }
        else
        {
            methodReason = $"{method} is the cheapest enabled method, at a {Money(chosen.FeeCents)} fee on this {amount} bill.";
        }

        var timingReason = scheduledFor is { } date
            ? $" Scheduling for {date:yyyy-MM-dd} (the due date) keeps your money until it's due."
            : daysUntilDue < 0
                ? " Paying now since this bill is past due."
                : $" Paying now since the due date ({bill.DueDate:yyyy-MM-dd}) is only {Math.Max(daysUntilDue, 0)} day(s) out.";

        return methodReason + timingReason;
    }

    /// <summary>Payer-facing money: sub-dollar amounts read as cents (95¢), otherwise dollars ($84.20).</summary>
    private static string Money(int cents) =>
        cents < 100
            ? $"{cents}¢"
            : $"${(cents / 100m).ToString("N2", CultureInfo.InvariantCulture)}";

    private static string MethodLabel(string method) => method switch
    {
        "ach" => "ACH",
        "card" => "card",
        "applepay" => "Apple Pay",
        "googlepay" => "Google Pay",
        "paypal" => "PayPal",
        _ => method,
    };
}
