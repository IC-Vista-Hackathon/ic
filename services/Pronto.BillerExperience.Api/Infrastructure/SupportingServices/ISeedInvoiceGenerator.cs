using Pronto.Invoice.Contracts.V1.Invoices;

namespace Pronto.BillerExperience.Api.Infrastructure.SupportingServices;

/// <summary>
/// Chooses biller-relevant demo invoice line items for onboarding seeding (FR-6). This is the
/// "agent configures" half of the seed path: the content is derived from what the biller actually
/// bills for (its name, website, vertical/bill type). The deterministic Invoice service then
/// persists the returned specs verbatim.
///
/// The seam mirrors <c>IExperienceDraftGenerator</c> — a deterministic implementation ships today
/// and an Azure OpenAI-backed one can slot in behind the same interface without changing callers.
/// </summary>
public interface ISeedInvoiceGenerator
{
    /// <summary>How the specs were produced (surfaced for observability), e.g. "deterministic".</summary>
    string Provider { get; }

    /// <summary>
    /// Produce up to <paramref name="count"/> biller-relevant demo invoice specs. The result must be
    /// stable for a given biller (reproducible) yet distinct across different billers.
    /// </summary>
    IReadOnlyList<SeedInvoiceSpec> Generate(SeedBillerContext biller, int count);
}
