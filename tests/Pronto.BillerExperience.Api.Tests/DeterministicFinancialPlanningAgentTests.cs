using Pronto.BillerExperience.Api.Application.Agents;
using Pronto.BillerExperience.Api.Domain;
using Pronto.Invoice.Contracts.V1.Invoices;
using Pronto.Payment.Contracts.V1.Payments;
using Xunit;

namespace Pronto.BillerExperience.Api.Tests;

/// <summary>
/// The Financial Planning stage must pick the cheapest quote for the payer, copy that quote's
/// fee/total verbatim (never recompute), and schedule only when the due date is comfortably out.
/// These cover the fee crossover, the absorbed-fee case, the timing boundary, determinism, and the
/// no-quotes rejection.
/// </summary>
public sealed class DeterministicFinancialPlanningAgentTests
{
    private const string BillerId = "b_1";
    private const string InvoiceId = "i_77";

    private static readonly DeterministicFinancialPlanningAgent Agent = new();

    private static BillSummary Bill(int amountCents, DateOnly dueDate) =>
        new(InvoiceId, amountCents, dueDate, "Water — July", InvoiceStatus.Due);

    private static PaymentQuoteResponse Quote(string method, int amountCents, int feeCents, int totalCents) =>
        new(BillerId, InvoiceId, method, amountCents, feeCents, totalCents);

    // Above ~$60 the card percent (2.5%) overtakes ACH's flat $1.50, so ACH becomes cheaper.
    [Fact]
    public async Task PicksAchWhenCardPercentExceedsAchFlatOnLargerBill()
    {
        var bill = Bill(8420, new DateOnly(2026, 7, 25));
        var quotes = new[]
        {
            Quote("card", 8420, 211, 8631), // 2.5% of $84.20 = $2.11
            Quote("ach", 8420, 150, 8570),  // flat $1.50
        };

        var plan = await Agent.PlanAsync(bill, quotes, new DateOnly(2026, 7, 14), CancellationToken.None);

        Assert.Equal("ach", plan.Method);
        Assert.Equal(150, plan.FeeCents);
        Assert.Equal(8570, plan.TotalCents);
    }

    // Below the crossover the card fee is the smaller one, so card wins.
    [Fact]
    public async Task PicksCardWhenCardPercentIsBelowAchFlatOnSmallBill()
    {
        var bill = Bill(2000, new DateOnly(2026, 7, 25));
        var quotes = new[]
        {
            Quote("card", 2000, 50, 2050),  // 2.5% of $20.00 = $0.50
            Quote("ach", 2000, 150, 2150),  // flat $1.50
        };

        var plan = await Agent.PlanAsync(bill, quotes, new DateOnly(2026, 7, 14), CancellationToken.None);

        Assert.Equal("card", plan.Method);
        Assert.Equal(50, plan.FeeCents);
        Assert.Equal(2050, plan.TotalCents);
    }

    // payer_pays_fee off: total == amount even though a fee is reported for display.
    [Fact]
    public async Task CopiesAbsorbedFeeVerbatimTotalEqualsAmount()
    {
        var bill = Bill(8420, new DateOnly(2026, 7, 25));
        var quotes = new[] { Quote("ach", 8420, 150, 8420) };

        var plan = await Agent.PlanAsync(bill, quotes, new DateOnly(2026, 7, 14), CancellationToken.None);

        Assert.Equal(150, plan.FeeCents);
        Assert.Equal(8420, plan.TotalCents);
        Assert.Equal(bill.AmountCents, plan.TotalCents);
    }

    [Fact]
    public async Task SchedulesOnDueDateWhenMoreThanThreeDaysOut()
    {
        var bill = Bill(8420, new DateOnly(2026, 7, 25));
        var quotes = new[] { Quote("ach", 8420, 150, 8570) };

        var plan = await Agent.PlanAsync(bill, quotes, new DateOnly(2026, 7, 14), CancellationToken.None);

        Assert.False(plan.PayNow);
        Assert.Equal(new DateOnly(2026, 7, 25), plan.ScheduledFor);
    }

    // Exactly 3 days out is the boundary: not "more than 3", so pay now.
    [Fact]
    public async Task PaysNowWhenDueDateIsAtOrWithinThreshold()
    {
        var bill = Bill(8420, new DateOnly(2026, 7, 17));
        var quotes = new[] { Quote("ach", 8420, 150, 8570) };

        var plan = await Agent.PlanAsync(bill, quotes, new DateOnly(2026, 7, 14), CancellationToken.None);

        Assert.True(plan.PayNow);
        Assert.Null(plan.ScheduledFor);
    }

    [Fact]
    public async Task PaysNowWhenPastDue()
    {
        var bill = Bill(8420, new DateOnly(2026, 7, 10));
        var quotes = new[] { Quote("ach", 8420, 150, 8570) };

        var plan = await Agent.PlanAsync(bill, quotes, new DateOnly(2026, 7, 14), CancellationToken.None);

        Assert.True(plan.PayNow);
        Assert.Contains("past due", plan.Rationale, StringComparison.OrdinalIgnoreCase);
    }

    // Equal totals must resolve the same way every run (fee, then method ordinal).
    [Fact]
    public async Task IsDeterministicOnTiedTotals()
    {
        var bill = Bill(5000, new DateOnly(2026, 7, 25));
        var quotes = new[]
        {
            Quote("card", 5000, 125, 5125),
            Quote("ach", 5000, 125, 5125),
        };

        var first = await Agent.PlanAsync(bill, quotes, new DateOnly(2026, 7, 14), CancellationToken.None);
        var second = await Agent.PlanAsync(bill, quotes.Reverse().ToArray(), new DateOnly(2026, 7, 14), CancellationToken.None);

        Assert.Equal("ach", first.Method); // "ach" < "card" ordinally
        Assert.Equal(first.Method, second.Method);
    }

    [Fact]
    public async Task ThrowsWhenNoQuotes()
    {
        var bill = Bill(8420, new DateOnly(2026, 7, 25));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Agent.PlanAsync(bill, Array.Empty<PaymentQuoteResponse>(), new DateOnly(2026, 7, 14), CancellationToken.None).AsTask());
    }
}
