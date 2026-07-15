using Pronto.BillerExperience.Api.Domain;
using Pronto.BillerExperience.Api.Infrastructure.Mcp.ServiceClients;

namespace Pronto.BillerExperience.Api.Application.Agents;

/// <summary>
/// The Bill Intelligence stage — the first of the payer pipeline. It finds the payer's invoice and
/// distills it into a <see cref="BillSummary"/> for Financial Planning. This deterministic
/// implementation is the demo default and the Foundry-down fallback; the eventual Azure agent adds
/// the natural-language reading of the invoice on top of the same lookup.
/// </summary>
public interface IBillIntelligenceAgent
{
    ValueTask<BillSummary> SummarizeAsync(string billerId, string invoiceId, CancellationToken cancellationToken);
}

/// <summary>
/// Reads the invoice through the same typed client that backs the <c>get_invoice</c> MCP tool and
/// projects it to a <see cref="BillSummary"/>. It does not choose a method or timing — that is
/// Financial Planning's job — and it never fabricates: a missing invoice surfaces as a
/// <see cref="KeyNotFoundException"/> (the API maps that to 404).
/// </summary>
public sealed class DeterministicBillIntelligenceAgent(IInvoiceServiceClient invoices) : IBillIntelligenceAgent
{
    public async ValueTask<BillSummary> SummarizeAsync(string billerId, string invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await invoices.GetAsync(billerId, invoiceId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Invoice {invoiceId} was not found for this biller.");

        return new BillSummary(
            invoice.Id,
            invoice.AmountCents,
            invoice.DueDate,
            invoice.Description,
            invoice.Status);
    }
}
