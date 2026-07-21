using System.Globalization;
using Pronto.BillerExperience.Api.Domain;
using Pronto.Invoice.Contracts.V1.Invoices;

namespace Pronto.BillerExperience.Api.Application.PayerChat;

/// <summary>
/// Request for one payer-chat turn. For this Financial-Planning slice the invoice is addressed
/// explicitly by <see cref="InvoiceId"/>; the natural-language path (deriving the invoice from
/// <see cref="Messages"/> + account number) arrives with the Foundry Bill Intelligence agent.
/// </summary>
public sealed record PayerChatRequest(
    string InvoiceId,
    string? AccountNumber = null,
    string? PayerAccountId = null,
    IReadOnlyList<PayerChatMessage>? Messages = null);

public sealed record PayerChatMessage(string Role, string Content);

/// <summary>Response for one payer-chat turn: a payer-facing reply plus the pipeline's artifacts.</summary>
public sealed record PayerChatResponse(string Reply, PayerChatArtifacts Artifacts);

public sealed record PayerChatArtifacts(
    BillSummaryView BillSummary,
    PaymentPlanView PaymentPlan,
    PayerChatAction? Action = null);

/// <summary>
/// An actionable follow-up the payer can take directly from the chat. Present only when the payer
/// expresses intent to pay ("pay it now") — the assistant surfaces a confirm control, but the
/// payer's explicit tap is still the confirmation; the assistant never submits on its own.
/// </summary>
public sealed record PayerChatAction(string Kind, string Method, int TotalCents, string? ScheduledFor)
{
    public const string ConfirmPayment = "confirm_payment";
}

/// <summary>Wire projection of the <see cref="BillSummary"/> artifact.</summary>
public sealed record BillSummaryView(
    string InvoiceId,
    int AmountCents,
    DateOnly DueDate,
    string Description,
    InvoiceStatus Status)
{
    public static BillSummaryView From(BillSummary bill) =>
        new(bill.InvoiceId, bill.AmountCents, bill.DueDate, bill.Description, bill.Status);
}

/// <summary>
/// Wire projection of the <see cref="PaymentPlan"/> artifact. <see cref="When"/> is the scheduled
/// date (<c>yyyy-MM-dd</c>) or the literal <c>now</c> for an immediate payment, matching
/// design/contracts.md's payment_plan shape.
/// </summary>
public sealed record PaymentPlanView(
    string Method,
    string When,
    int FeeCents,
    int TotalCents,
    string Rationale)
{
    public static PaymentPlanView From(PaymentPlan plan) =>
        new(
            plan.Method,
            plan.ScheduledFor?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "now",
            plan.FeeCents,
            plan.TotalCents,
            plan.Rationale);
}
