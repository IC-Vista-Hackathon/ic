using Pronto.BillerExperience.Api.Application.PayerChat;
using Pronto.BillerExperience.Api.Domain;
using Pronto.Invoice.Contracts.V1.Invoices;
using Pronto.Payment.Contracts.V1.Payments;
using Xunit;

namespace Pronto.BillerExperience.Api.Tests;

/// <summary>
/// The responder turns a payer's free-text question into a grounded reply — always from the
/// pipeline's bill/plan/quote artifacts, never inventing numbers. With no question it returns the
/// opening recommendation; a keyworded follow-up is answered in context.
/// </summary>
public sealed class PayerChatResponderTests
{
    private static readonly BillSummary Bill =
        new("i_1", 8420, new DateOnly(2026, 7, 29), "Water & sewer service", InvoiceStatus.Due);

    private static readonly PaymentPlan Plan =
        new("ach", new DateOnly(2026, 7, 29), 150, 8570, "ACH's $1.50 fee beats card's $2.11 on this $84.20 bill.");

    private static readonly IReadOnlyList<PaymentQuoteResponse> Quotes =
    [
        new("b_1", "i_1", "ach", 8420, 150, 8570),
        new("b_1", "i_1", "card", 8420, 211, 8631),
    ];

    [Fact]
    public void NoQuestionReturnsOpeningRecommendation()
    {
        var reply = PayerChatResponder.Reply(null, Bill, Plan, Quotes);

        Assert.Contains("Water & sewer service", reply);
        Assert.Contains(Plan.Rationale, reply);
    }

    [Fact]
    public void FeeQuestionComparesEveryMethod()
    {
        var reply = PayerChatResponder.Reply("what are the fees?", Bill, Plan, Quotes);

        Assert.Contains("$1.50", reply);
        Assert.Contains("$2.11", reply);
        Assert.Contains("Bank account (ACH)", reply);
    }

    [Fact]
    public void FeeQuestionReportsNoAddedFeeWhenTheBillerAbsorbsIt()
    {
        // Absorbed fee: the quote still reports FeeCents for display, but TotalCents == AmountCents,
        // so the payer pays nothing extra. The reply must not claim a fee the payer doesn't pay.
        var absorbedPlan = new PaymentPlan("ach", new DateOnly(2026, 7, 29), 0, 8420, "No added fee on this $84.20 bill.");
        IReadOnlyList<PaymentQuoteResponse> absorbedQuotes =
        [
            new("b_1", "i_1", "ach", 8420, 150, 8420),
            new("b_1", "i_1", "card", 8420, 211, 8420),
        ];

        var reply = PayerChatResponder.Reply("what are the fees?", Bill, absorbedPlan, absorbedQuotes);

        Assert.Contains("no added fee", reply);
        Assert.DoesNotContain("$1.50", reply);
        Assert.DoesNotContain("$2.11", reply);
    }

    [Fact]
    public void TimingQuestionExplainsTheSchedule()
    {
        var reply = PayerChatResponder.Reply("when will it be paid?", Bill, Plan, Quotes);

        Assert.Contains("July 29, 2026", reply);
    }

    [Fact]
    public void MethodQuestionContrastsWithTheRecommendation()
    {
        var reply = PayerChatResponder.Reply("what about paying by card?", Bill, Plan, Quotes);

        Assert.Contains("Card", reply);
        Assert.Contains("Bank account (ACH)", reply);
    }

    [Fact]
    public void PayNowIntentOffersAnInChatConfirmControl()
    {
        var reply = PayerChatResponder.Reply("ok let's pay it now", Bill, Plan, Quotes);

        Assert.Contains("Confirm & pay", reply);
        Assert.Contains("$85.70", reply);
        // The assistant surfaces the control but never submits itself — the payer's tap confirms.
        Assert.Contains("can't submit it for you", reply);
    }

    [Fact]
    public void PayNowIntentSurfacesAConfirmPaymentAction()
    {
        var action = PayerChatResponder.DetectAction("ok let's pay it now", Plan);

        Assert.NotNull(action);
        Assert.Equal(PayerChatAction.ConfirmPayment, action!.Kind);
        Assert.Equal(Plan.Method, action.Method);
        Assert.Equal(Plan.TotalCents, action.TotalCents);
        Assert.Equal("2026-07-29", action.ScheduledFor);
    }

    [Fact]
    public void QuestionsDoNotSurfaceAConfirmAction()
    {
        // A question — even one containing "pay" — must not surface a confirm control.
        Assert.Null(PayerChatResponder.DetectAction("what's the best way to pay?", Plan));
        Assert.Null(PayerChatResponder.DetectAction("what are the fees?", Plan));
        // Schedule intent stays advisory (no confirm control), and no message means no action.
        Assert.Null(PayerChatResponder.DetectAction("can you schedule it for me", Plan));
        Assert.Null(PayerChatResponder.DetectAction(null, Plan));
    }

    [Fact]
    public void ScheduleIntentStaysAdvisoryAndDoesNotExecute()
    {
        var reply = PayerChatResponder.Reply("can you schedule it for me", Bill, Plan, Quotes);

        Assert.Contains("can't schedule or submit the payment for you", reply);
        Assert.Contains("July 29, 2026", reply);
    }

    [Fact]
    public void BestWayQuestionStillExplainsTheRecommendation()
    {
        // "what's the best way to pay?" is a question, not an action — it must not be swallowed
        // by the pay-now intent even though it contains the word "pay".
        var reply = PayerChatResponder.Reply("what's the best way to pay?", Bill, Plan, Quotes);

        Assert.Contains(Plan.Rationale, reply);
    }

    [Fact]
    public void UnknownQuestionFallsBackToGuidance()
    {
        var reply = PayerChatResponder.Reply("hello there", Bill, Plan, Quotes);

        Assert.Contains("Ask me about", reply);
    }
}
