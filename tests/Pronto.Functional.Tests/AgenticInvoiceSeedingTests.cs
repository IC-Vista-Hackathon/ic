using Xunit;

namespace Pronto.Functional.Tests;

/// <summary>
/// FR-6 — Demo invoices seeded for the payer preview must be relevant to the biller, chosen
/// agentically from what the biller actually bills for — NOT a hard-coded set keyed on bill_type.
///
/// Seeding is now agentic: onboarding derives biller-relevant demo line items from the biller's
/// name, website, and vertical (see DeterministicSeedInvoiceGenerator) and the Invoice service
/// persists them, instead of returning a fixed HOA/insurance template keyed on bill_type. See
/// docs/pronto-functional-requirements.md (FR-6).
/// </summary>
[Trait(Categories.Name, Categories.Functional)]
public sealed class AgenticInvoiceSeedingTests
{
    private static readonly string[] UnrelatedTemplateMarkers =
        ["hoa", "special assessment", "all i want for christmas"];

    [SkippableFact]
    public async Task SeededInvoicesAreRelevantToBillerNotFixedTemplate()
    {
        ProntoEnvironment.RequireBaseUrl();
        using var client = new ProntoApiClient();

        // An online apparel store — nothing to do with an HOA.
        var created = await client.CreateBillerAsync(
            "Happy Pants NYC", billType: "other", website: "https://www.happypantsnyc.com");
        var billerId = created["biller"]!["biller_id"]!.GetValue<string>();

        var invoices = await client.ListSeededInvoicesAsync(billerId);
        Assert.NotEmpty(invoices);

        foreach (var invoice in invoices)
        {
            var text = invoice.InvoiceText();
            foreach (var marker in UnrelatedTemplateMarkers)
            {
                Assert.DoesNotContain(marker, text);
            }
        }
    }

    [SkippableFact]
    public async Task TwoUnrelatedBillersDoNotGetIdenticalSeededInvoices()
    {
        ProntoEnvironment.RequireBaseUrl();
        using var client = new ProntoApiClient();

        var apparel = await client.CreateBillerAsync(
            "Happy Pants NYC", billType: "other", website: "https://www.happypantsnyc.com");
        var civic = await client.CreateBillerAsync(
            "Riverside Parks District", billType: "other",
            website: "https://thankful-sea-0d2febf0f.7.azurestaticapps.net");

        var apparelInvoices = await client.ListSeededInvoicesAsync(apparel["biller"]!["biller_id"]!.GetValue<string>());
        var civicInvoices = await client.ListSeededInvoicesAsync(civic["biller"]!["biller_id"]!.GetValue<string>());

        var apparelLines = apparelInvoices.Select(i => i.InvoiceText()).OrderBy(t => t).ToArray();
        var civicLines = civicInvoices.Select(i => i.InvoiceText()).OrderBy(t => t).ToArray();

        Assert.False(
            apparelLines.SequenceEqual(civicLines),
            "Two unrelated billers received identical seeded invoices; seeding is a fixed template, not agentic.");
    }
}
