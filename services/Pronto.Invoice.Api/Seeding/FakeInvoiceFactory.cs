using Pronto.Invoice.Api.Domain;

namespace Pronto.Invoice.Api.Seeding;

/// <summary>
/// Generates demo invoices for onboarding. Deterministic (index-driven, no RNG) so a given
/// request produces a stable, previewable set — themed loosely by bill type.
/// </summary>
public static class FakeInvoiceFactory
{
    private const int DefaultCount = 4;
    private const int MaxCount = 24;

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

    private static string[] DescriptionsFor(string? billType)
    {
        var normalized = billType?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "utility" => ["Water & sewer service", "Electricity usage", "Waste collection", "Stormwater fee"],
            "real estate tax" or "property tax" =>
                ["Property tax installment", "Assessment adjustment", "Late parcel fee", "Supplemental levy"],
            "insurance" => ["Monthly premium", "Policy renewal", "Coverage adjustment", "Rider add-on"],
            _ => ["Monthly statement", "Service charge", "Account balance", "Recurring bill"],
        };
    }
}
