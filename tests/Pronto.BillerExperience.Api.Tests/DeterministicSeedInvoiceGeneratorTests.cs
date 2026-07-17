using Pronto.BillerExperience.Api.Infrastructure.SupportingServices;
using Pronto.Invoice.Contracts.V1.Invoices;
using Xunit;

namespace Pronto.BillerExperience.Api.Tests;

/// <summary>
/// FR-6: seeded demo invoices must be biller-relevant (not a fixed HOA/insurance template) and
/// distinct per biller. These in-process tests exercise the deterministic generator directly; the
/// deployed behavior is guarded by <c>AgenticInvoiceSeedingTests</c>.
/// </summary>
public sealed class DeterministicSeedInvoiceGeneratorTests
{
    private static readonly string[] UnrelatedTemplateMarkers =
        ["hoa", "special assessment", "all i want for christmas"];

    private readonly DeterministicSeedInvoiceGenerator _generator = new();

    private static SeedBillerContext Apparel(string id = "b-apparel") =>
        new(id, "Happy Pants NYC", "other", new Uri("https://www.happypantsnyc.com"));

    // The payer-visible text the deployed functional test inspects: description + type + note.
    private static string Text(SeedInvoiceSpec spec) =>
        string.Join(' ', new[] { spec.Description, spec.Type, spec.Note }
            .Where(s => !string.IsNullOrEmpty(s)))
            .ToLowerInvariant();

    [Fact]
    public void ApparelBillerGetsRelevantInvoicesNotHoaTemplate()
    {
        var specs = _generator.Generate(Apparel(), 4);

        Assert.NotEmpty(specs);
        foreach (var spec in specs)
        {
            var text = Text(spec);
            foreach (var marker in UnrelatedTemplateMarkers)
            {
                Assert.DoesNotContain(marker, text, StringComparison.Ordinal);
            }
        }

        // Relevance: at least one line reads as retail/apparel rather than a civic/HOA charge.
        Assert.Contains(specs, s => s.Description.Contains("apparel", StringComparison.OrdinalIgnoreCase)
            || s.Description.Contains("store", StringComparison.OrdinalIgnoreCase)
            || s.Description.Contains("order", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TwoUnrelatedBillersGetDifferentInvoiceSets()
    {
        var apparel = _generator.Generate(Apparel(), 4);
        var parks = _generator.Generate(
            new SeedBillerContext(
                "b-parks", "Riverside Parks District", "other",
                new Uri("https://thankful-sea-0d2febf0f.7.azurestaticapps.net")),
            4);

        var apparelText = apparel.Select(Text).OrderBy(t => t).ToArray();
        var parksText = parks.Select(Text).OrderBy(t => t).ToArray();

        Assert.False(apparelText.SequenceEqual(parksText));
    }

    [Fact]
    public void TwoSameVerticalBillersStillGetDistinctSets()
    {
        // Even two billers that classify into the same vertical must not receive identical text.
        var a = _generator.Generate(Apparel("b-apparel-1"), 4);
        var b = _generator.Generate(Apparel("b-apparel-2"), 4);

        var aText = a.Select(Text).OrderBy(t => t).ToArray();
        var bText = b.Select(Text).OrderBy(t => t).ToArray();

        Assert.False(aText.SequenceEqual(bText));
    }

    [Fact]
    public void GenerationIsStableForTheSameBiller()
    {
        var first = _generator.Generate(Apparel(), 4);
        var second = _generator.Generate(Apparel(), 4);

        Assert.Equal(
            first.Select(s => (s.Description, s.AmountCents, s.DueInDays, s.PayerName, s.Type)),
            second.Select(s => (s.Description, s.AmountCents, s.DueInDays, s.PayerName, s.Type)));
    }

    [Fact]
    public void GeneratesRequestedCountWithUsableData()
    {
        var specs = _generator.Generate(Apparel(), 4);

        Assert.Equal(4, specs.Count);
        Assert.All(specs, s =>
        {
            Assert.True(s.AmountCents > 0);
            Assert.True(s.DueInDays >= 0);
            Assert.False(string.IsNullOrWhiteSpace(s.Description));
        });
    }

    // ---- category-aware seeding (multi-invoice, F3 cart) ------------------------------------

    private static SeedBillerContext WithCategories(params SeedBillingCategory[] categories) =>
        new("b-utility", "Riverside Water", "utility", new Uri("https://riverside.example"))
        {
            Categories = categories,
        };

    [Fact]
    public void MultiCategoryBillerGetsAtLeastOneInvoicePerCategory()
    {
        var biller = WithCategories(
            new SeedBillingCategory("water", "Water & sewer", "Monthly"),
            new SeedBillingCategory("storm", "Stormwater", "Quarterly"),
            new SeedBillingCategory("waste", "Waste collection", "Annual"));

        var specs = _generator.Generate(biller, count: 3);

        Assert.Equal(3, specs.Count);
        Assert.Contains(specs, s => s.Type == "Water & sewer");
        Assert.Contains(specs, s => s.Type == "Stormwater");
        Assert.Contains(specs, s => s.Type == "Waste collection");
    }

    [Fact]
    public void CategoryCountBelowRequestedStillHonorsRequestedCountByRepeatingCategories()
    {
        var biller = WithCategories(
            new SeedBillingCategory("water", "Water & sewer", "Monthly"),
            new SeedBillingCategory("storm", "Stormwater", "Quarterly"));

        var specs = _generator.Generate(biller, count: 4);

        Assert.Equal(4, specs.Count);
        // Every category is covered, and a later occurrence is labelled distinctly (#2) for the cart.
        Assert.Contains(specs, s => s.Description.Contains("Water & sewer #2", StringComparison.Ordinal));
    }

    [Fact]
    public void CategoryCadenceDrivesDueDateOrdering()
    {
        var monthly = _generator.Generate(
            WithCategories(new SeedBillingCategory("m", "Monthly line", "Monthly")), count: 1)[0];
        var annual = _generator.Generate(
            WithCategories(new SeedBillingCategory("a", "Annual line", "Annual")), count: 1)[0];

        // A monthly bill reads as due sooner than an annual one.
        Assert.True(monthly.DueInDays < annual.DueInDays);
    }

    [Fact]
    public void CategoryGenerationIsDeterministicForTheSameBillerAndProfile()
    {
        var biller = WithCategories(
            new SeedBillingCategory("water", "Water & sewer", "Monthly"),
            new SeedBillingCategory("storm", "Stormwater", "Quarterly"));

        var first = _generator.Generate(biller, count: 4);
        var second = _generator.Generate(biller, count: 4);

        Assert.Equal(
            first.Select(s => (s.Description, s.AmountCents, s.DueInDays, s.PayerName, s.Type)),
            second.Select(s => (s.Description, s.AmountCents, s.DueInDays, s.PayerName, s.Type)));
    }
}
