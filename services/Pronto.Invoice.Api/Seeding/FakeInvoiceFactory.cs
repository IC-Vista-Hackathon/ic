using System.Buffers.Binary;
using Pronto.Invoice.Api.Domain;
using SeedInvoiceSpec = Pronto.Invoice.Contracts.V1.Invoices.SeedInvoiceSpec;

namespace Pronto.Invoice.Api.Seeding;

/// <summary>
/// Materializes demo invoices for onboarding.
/// </summary>
/// <remarks>
/// Per "agents configure, deterministic services execute" the biller-relevant <em>content</em> is
/// chosen upstream (the Biller Experience side, which owns onboarding and the biller profile) and
/// passed in as <see cref="SeedInvoiceSpec"/>s; this factory just stamps biller/account scoping, a
/// lifecycle status of <c>due</c>, and anchors relative due dates to the caller's clock.
///
/// When no specs are supplied it falls back to a generic, index-driven demo set (optionally themed
/// by <c>bill_type</c>). It deliberately does <b>not</b> hand-author a fixed set keyed on
/// <c>bill_type</c> — that is the FR-6 defect this replaced: it made an apparel store onboarded as
/// <c>other</c> receive HOA invoices and gave two unrelated billers byte-identical sets.
/// </remarks>
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
    /// Build a seed set for a biller. When <paramref name="specs"/> is non-empty the caller-chosen,
    /// biller-relevant line items are persisted verbatim (only biller/account/status/due-date are
    /// stamped here). Otherwise a generic index-driven set is produced; <paramref name="count"/> and
    /// <paramref name="billType"/> shape that fallback. <paramref name="today"/> anchors due dates.
    /// </summary>
    public static IReadOnlyList<InvoiceDocument> Create(
        string billerId,
        string accountNumber,
        int? count,
        string? billType,
        DateOnly today,
        IReadOnlyList<SeedInvoiceSpec>? specs = null)
    {
        if (specs is { Count: > 0 })
        {
            return specs
                .Take(MaxCount)
                .Select((spec, index) => FromSpec(spec, billerId, accountNumber, index, today))
                .ToList();
        }

        var n = Math.Clamp(count ?? DefaultCount, 1, MaxCount);
        var descriptions = DescriptionsFor(billType);

        var invoices = new List<InvoiceDocument>(n);
        for (var i = 0; i < n; i++)
        {
            var description = descriptions[i % descriptions.Length];
            invoices.Add(new InvoiceDocument
            {
                Id = SeedId(billerId, accountNumber, i),
                BillerId = billerId,
                AccountNumber = accountNumber,
                PayerName = PayerNames[i % PayerNames.Length],
                Description = description,
                AmountCents = AmountsCents[i % AmountsCents.Length],
                // Stagger due dates a few weeks out so the demo shows a realistic spread.
                DueDate = today.AddDays(14 + (i * 7)),
                Status = InvoiceStatus.Due,
            });
        }

        return invoices;
    }

    private static InvoiceDocument FromSpec(
        SeedInvoiceSpec spec,
        string billerId,
        string accountNumber,
        int index,
        DateOnly today) => new()
    {
        Id = SeedId(billerId, accountNumber, index),
        BillerId = billerId,
        AccountNumber = accountNumber,
        PayerName = string.IsNullOrWhiteSpace(spec.PayerName)
            ? PayerNames[index % PayerNames.Length]
            : spec.PayerName.Trim(),
        Description = spec.Description,
        AmountCents = spec.AmountCents,
        DueDate = today.AddDays(spec.DueInDays),
        Status = InvoiceStatus.Due,
        Type = spec.Type,
        StatusColor = spec.StatusColor,
        Note = spec.Note,
        NoteEmphasis = spec.NoteEmphasis,
    };

    /// <summary>
    /// A stable invoice id derived from the biller, account, and the invoice's <em>slot</em> in the
    /// seed set (its position, not its content). Re-seeding the same account overwrites slot-for-slot
    /// via upsert, so re-publishing after onboarding changed the profile replaces the earlier demo
    /// invoices instead of piling a second set alongside them (the account reflects only the latest
    /// seed). Deriving the id from the description instead would let a changed category set produce
    /// new ids and accumulate stale invoices. The Cosmos and in-memory repositories both upsert by
    /// <c>id</c>.
    /// </summary>
    private static string SeedId(string billerId, string accountNumber, int slot)
    {
        var lo = Fnv1a($"{billerId}|{accountNumber}|{slot}");
        var hi = Fnv1a($"{slot}|{accountNumber}|{billerId}");
        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[..8], lo);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[8..], hi);
        return new Guid(bytes).ToString();
    }

    // FNV-1a (64-bit): process-stable (unlike string.GetHashCode) so ids reproduce across runs.
    private static ulong Fnv1a(string value)
    {
        const ulong offset = 14695981039346656037;
        const ulong prime = 1099511628211;
        var hash = offset;
        foreach (var ch in value)
        {
            hash ^= ch;
            hash *= prime;
        }

        return hash;
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
}
