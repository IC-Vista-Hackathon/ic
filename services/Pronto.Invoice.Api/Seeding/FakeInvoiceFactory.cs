using Pronto.Invoice.Api.Domain;

namespace Pronto.Invoice.Api.Seeding;

/// <summary>
/// Generates demo invoices for onboarding. Deterministic (index-driven, no RNG) so a given
/// request produces a stable, previewable set — themed loosely by bill type.
/// </summary>
/// <remarks>
/// For the hackathon demo, two bill types produce hand-authored, fixed sets so the payer
/// experience shows a realistic, curated story: <c>insurance</c> (auto/home/life policies) and
/// <c>other</c> (the HOA demo). These sets ignore <c>count</c>. Every other bill type keeps the
/// original index-driven generation.
/// </remarks>
public static class FakeInvoiceFactory
{
    private const int DefaultCount = 4;
    private const int MaxCount = 24;

    // Demo status colors (presentation only — the payment lifecycle status stays "due").
    private const string Green = "green";
    private const string Yellow = "yellow";

    // Curated payer for the hand-authored demo sets so every bill reads as one household/policyholder.
    private const string DemoPayerName = "Alex Rivera";

    private static readonly string[] PayerNames =
    [
        "Alex Rivera", "Jordan Chen", "Sam Okafor", "Priya Nair",
        "Morgan Lee", "Diego Santos", "Fatima Hassan", "Taylor Brooks",
    ];

    private static readonly int[] AmountsCents =
    [
        8420, 12500, 4599, 23110, 6750, 15980, 3299, 9900,
    ];

    /// <summary>
    /// Build a seed set for a biller. <paramref name="count"/> and <paramref name="billType"/>
    /// are optional; <paramref name="accountNumber"/> is the account all invoices attach to.
    /// <paramref name="today"/> anchors due dates (kept as a parameter for testability).
    /// </summary>
    public static IReadOnlyList<InvoiceDocument> Create(
        string billerId,
        string accountNumber,
        int? count,
        string? billType,
        DateOnly today)
    {
        var curated = CuratedSetFor(billType);
        if (curated is not null)
        {
            return curated
                .Select(spec => spec.ToDocument(billerId, accountNumber))
                .ToList();
        }

        var n = Math.Clamp(count ?? DefaultCount, 1, MaxCount);
        var descriptions = DescriptionsFor(billType);

        var invoices = new List<InvoiceDocument>(n);
        for (var i = 0; i < n; i++)
        {
            invoices.Add(new InvoiceDocument
            {
                Id = Guid.NewGuid().ToString(),
                BillerId = billerId,
                AccountNumber = accountNumber,
                PayerName = PayerNames[i % PayerNames.Length],
                Description = descriptions[i % descriptions.Length],
                AmountCents = AmountsCents[i % AmountsCents.Length],
                // Stagger due dates a few weeks out so the demo shows a realistic spread.
                DueDate = today.AddDays(14 + (i * 7)),
                Status = InvoiceStatus.Due,
            });
        }

        return invoices;
    }

    /// <summary>
    /// Hand-authored demo sets keyed by biller <c>bill_type</c> (the Studio vertical id). Returns
    /// null for bill types that use the generic index-driven generation instead.
    /// </summary>
    private static IReadOnlyList<DemoInvoice>? CuratedSetFor(string? billType)
    {
        var normalized = billType?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "insurance" =>
            [
                new DemoInvoice("Auto", "Auto policy premium", 14250, new DateOnly(2026, 7, 14), Yellow,
                    "Overdue but in the grace period — pay today to keep your policy active with no penalty.", NoteEmphasis: true),
                new DemoInvoice("Home", "Homeowners policy premium", 8900, new DateOnly(2026, 8, 30), Green),
                new DemoInvoice("Life", "Life policy premium", 4500, new DateOnly(2026, 12, 31), Green),
            ],
            "other" =>
            [
                new DemoInvoice("HOA Dues", "Quarterly HOA dues", 35000, new DateOnly(2026, 7, 31), Green),
                new DemoInvoice("Special Assessment (Pool)", "Community pool special assessment", 450000, new DateOnly(2026, 12, 31), Green,
                    "This assessment is much larger than your other bills — a payment plan is recommended.", NoteEmphasis: true),
                new DemoInvoice("HOA Fine", "$100 fine for playing \"All I Want for Christmas is You\" during summer", 10000, new DateOnly(2026, 7, 31), Green),
            ],
            _ => null,
        };
    }

    private static string[] DescriptionsFor(string? billType)
    {
        var normalized = billType?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "utility" or "utilities" => ["Water & sewer service", "Electricity usage", "Waste collection", "Stormwater fee"],
            "real estate tax" or "property tax" or "tax" =>
                ["Property tax installment", "Assessment adjustment", "Late parcel fee", "Supplemental levy"],
            _ => ["Monthly statement", "Service charge", "Account balance", "Recurring bill"],
        };
    }

    /// <summary>A single hand-authored demo invoice, materialized into a stored document on seed.</summary>
    private sealed record DemoInvoice(
        string Type,
        string Description,
        int AmountCents,
        DateOnly DueDate,
        string StatusColor,
        string? Note = null,
        bool NoteEmphasis = false)
    {
        public InvoiceDocument ToDocument(string billerId, string accountNumber) => new()
        {
            Id = Guid.NewGuid().ToString(),
            BillerId = billerId,
            AccountNumber = accountNumber,
            PayerName = DemoPayerName,
            Description = Description,
            AmountCents = AmountCents,
            DueDate = DueDate,
            Status = InvoiceStatus.Due,
            Type = Type,
            StatusColor = StatusColor,
            Note = Note,
            NoteEmphasis = NoteEmphasis,
        };
    }
}
