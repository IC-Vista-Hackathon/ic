using System.Globalization;
using Pronto.BillerExperience.Api.Domain;
using Pronto.Payment.Contracts.V1.Payments;

namespace Pronto.BillerExperience.Api.Application.PayerChat;

/// <summary>
/// Turns a payer's free-text question into a grounded reply about their bill and the plan the
/// pipeline chose. Deterministic and offline — the same role the Foundry payer agents fill in
/// production — so it answers only from the pipeline artifacts (bill, plan, server quotes) and
/// never invents numbers. When there is no question (the opening turn), it returns the standard
/// recommendation summary.
/// </summary>
internal static class PayerChatResponder
{
    public static string Reply(
        string? question,
        BillSummary bill,
        PaymentPlan plan,
        IReadOnlyList<PaymentQuoteResponse> quotes)
    {
        var opening = $"Your {bill.Description} bill is due {bill.DueDate:MMMM d, yyyy}. {plan.Rationale}";
        if (string.IsNullOrWhiteSpace(question))
        {
            return opening;
        }

        var text = question.ToLowerInvariant();

        // Action intent: the payer wants to proceed, not ask a question. The assistant surfaces a
        // confirm control (see DetectAction) but never submits on its own — the payer's explicit
        // tap is the confirmation, so the explicit-confirmation gate is never bypassed.
        if (IsScheduleIntent(text))
        {
            var when = plan.ScheduledFor is { } scheduled
                ? $"on the {scheduled:MMMM d, yyyy} due date"
                : "now, since the due date is close";
            return $"I can't schedule or submit the payment for you — you're always the one who confirms. "
                + $"When you're ready, pick {MethodLabel(plan.Method)} below and complete the review step; "
                + $"I'd pay {Money(plan.TotalCents)} {when}.";
        }

        if (IsPayNowIntent(text))
        {
            return $"You've got it — I can't submit it for you, but you can confirm right here: "
                + $"the {MethodLabel(plan.Method)} total is {Money(plan.TotalCents)}. "
                + $"Tap \u201cConfirm \u0026 pay\u201d below to complete it.";
        }

        if (MentionsAny(text, "fee", "cost", "charge", "how much", "cheap", "expensive", "compare"))
        {
            var lines = quotes
                .OrderBy(quote => quote.TotalCents)
                .Select(quote => $"{MethodLabel(quote.Method)} has {FeePhrase(quote, bill.AmountCents)} ({Money(quote.TotalCents)} total)");
            return $"On this {Money(bill.AmountCents)} bill: {string.Join("; ", lines)}. "
                + $"I recommend {MethodLabel(plan.Method)} — the lowest total at {Money(plan.TotalCents)}.";
        }

        if (MentionsAny(text, "when", "due", "date", "schedule", "later", "now", "time"))
        {
            return plan.ScheduledFor is { } scheduled
                ? $"Your bill is due {bill.DueDate:MMMM d, yyyy}. I'd schedule the payment for {scheduled:MMMM d, yyyy} (the due date) so your money stays with you until it's owed. You can still pay now if you prefer."
                : $"Your bill is due {bill.DueDate:MMMM d, yyyy}, which is close, so I'd pay now rather than schedule it.";
        }

        if (MentionsAny(text, "card", "credit", "debit"))
        {
            return DescribeMethod("card", plan, quotes, bill.AmountCents)
                ?? "This biller doesn't accept card payments for this bill.";
        }

        if (MentionsAny(text, "ach", "bank", "checking", "account", "transfer"))
        {
            return DescribeMethod("ach", plan, quotes, bill.AmountCents)
                ?? "This biller doesn't accept bank-account (ACH) payments for this bill.";
        }

        if (MentionsAny(text, "why", "recommend", "suggest", "best", "should"))
        {
            return $"{plan.Rationale} You're always free to pick a different method below.";
        }

        if (MentionsAny(text, "autopay", "auto pay", "automatic", "recurring", "every month"))
        {
            return "AutoPay uses your chosen method for future bills automatically — it's optional, you can enroll from the payment screen, and you can cancel anytime.";
        }

        if (MentionsAny(text, "paperless", "email", "mail", "statement"))
        {
            return "Paperless billing sends your bills by email instead of by mail. It's optional and you can turn it on from the payment screen.";
        }

        if (MentionsAny(text, "total", "owe", "balance", "amount", "pay"))
        {
            return $"Your {bill.Description} bill is {Money(bill.AmountCents)}. With {MethodLabel(plan.Method)} the total including the fee is {Money(plan.TotalCents)}.";
        }

        return $"{opening} Ask me about the fees, when it's due, or a specific method (card or bank account), and I'll walk you through it.";
    }

    private static string? DescribeMethod(
        string method,
        PaymentPlan plan,
        IReadOnlyList<PaymentQuoteResponse> quotes,
        int amountCents)
    {
        var quote = quotes.FirstOrDefault(item =>
            string.Equals(item.Method, method, StringComparison.OrdinalIgnoreCase));
        if (quote is null)
        {
            return null;
        }

        var label = MethodLabel(method);
        var detail = $"{label} has {FeePhrase(quote, amountCents)}, {Money(quote.TotalCents)} total.";
        if (string.Equals(plan.Method, method, StringComparison.OrdinalIgnoreCase))
        {
            return $"{detail} That's the one I recommend — it's the lowest total.";
        }

        var recommended = quotes.FirstOrDefault(item =>
            string.Equals(item.Method, plan.Method, StringComparison.OrdinalIgnoreCase));
        return recommended is null
            ? detail
            : $"{detail} It's more than {MethodLabel(plan.Method)} ({Money(recommended.TotalCents)} total), which is why I recommend {MethodLabel(plan.Method)} — but you can choose {label} if you'd rather.";
    }

    /// <summary>
    /// The confirmable action a pay-now intent surfaces: pay the recommended method for the plan's
    /// total. Returns null for anything else — questions never surface a confirm control, and a
    /// schedule intent stays advisory. The payer still taps to confirm; this only offers the button.
    /// </summary>
    public static PayerChatAction? DetectAction(string? question, PaymentPlan plan)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return null;
        }

        var text = question.ToLowerInvariant();
        if (IsScheduleIntent(text) || !IsPayNowIntent(text))
        {
            return null;
        }

        var scheduledFor = plan.ScheduledFor?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return new PayerChatAction(PayerChatAction.ConfirmPayment, plan.Method, plan.TotalCents, scheduledFor);
    }

    // The fee the payer actually pays is total − bill amount (zero when the biller absorbs it), not
    // the quote's raw FeeCents, which is reported for display even when the biller covers it.
    private static string FeePhrase(PaymentQuoteResponse quote, int amountCents)
    {
        var payerFeeCents = quote.TotalCents - amountCents;
        return payerFeeCents <= 0 ? "no added fee" : $"a {Money(payerFeeCents)} fee";
    }

    private static bool IsScheduleIntent(string text) =>
        MentionsAny(text, "schedule it", "schedule for", "schedule my", "schedule the", "schedule this", "can you schedule", "please schedule", "set it up", "book it");

    private static bool IsPayNowIntent(string text) =>
        MentionsAny(text, "pay it now", "pay now", "pay today", "pay it", "pay this", "pay the bill", "let's pay", "lets pay", "make the payment", "make payment", "submit the payment", "submit payment", "go ahead", "do it", "proceed", "just pay");

    private static bool MentionsAny(string text, params string[] terms) =>
        terms.Any(term => text.Contains(term, StringComparison.Ordinal));

    private static string MethodLabel(string method) => method.ToLowerInvariant() switch
    {
        "ach" => "Bank account (ACH)",
        "card" => "Card",
        _ => method,
    };

    private static string Money(int cents) => cents < 100
        ? $"{cents}¢"
        : $"${(cents / 100m).ToString("N2", CultureInfo.InvariantCulture)}";
}
