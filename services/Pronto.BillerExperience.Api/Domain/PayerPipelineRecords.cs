using Pronto.Invoice.Contracts.V1.Invoices;

namespace Pronto.BillerExperience.Api.Domain;

/// <summary>
/// Inter-step artifacts of the payer pipeline (Bill Intelligence → Financial Planning → Policy →
/// Execution). These flow between in-process orchestration steps, like
/// <see cref="DraftGenerationResult"/> on the biller side; they are not wire contracts. The
/// payer-chat response DTO is a separate, versioned shape (see design/contracts.md).
/// </summary>

/// <summary>
/// What Bill Intelligence hands to Financial Planning: the payer's bill, distilled to the fields a
/// plan is built from. Bill Intelligence produces this from <c>get_invoice</c>; Financial Planning
/// reasons over it and never looks the invoice up itself.
/// </summary>
public sealed record BillSummary(
    string InvoiceId,
    int AmountCents,
    DateOnly DueDate,
    string Description,
    InvoiceStatus Status);

/// <summary>
/// What Financial Planning hands to Policy: the concrete plan. <see cref="ScheduledFor"/> is
/// <see langword="null"/> for pay-now and a date for pay-later (matching the Payment entity's
/// <c>scheduled_for</c>, see design/entities.md). <see cref="FeeCents"/> and
/// <see cref="TotalCents"/> are copied verbatim from the chosen server quote — Financial Planning
/// selects a quote, it never computes fees — so the numbers the payer approves are the numbers the
/// Payment Service will charge.
/// </summary>
public sealed record PaymentPlan(
    string Method,
    DateOnly? ScheduledFor,
    int FeeCents,
    int TotalCents,
    string Rationale)
{
    /// <summary>True when the plan pays immediately rather than scheduling for a later date.</summary>
    public bool PayNow => ScheduledFor is null;
}
