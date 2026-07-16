using Pronto.Invoice.Contracts.V1.Invoices;

namespace Pronto.BillerExperience.Api.Infrastructure.SupportingServices;

/// <summary>
/// Deterministic implementation of <see cref="ISeedInvoiceGenerator"/>. It classifies the biller
/// into a billing vertical from signals in its name, website, and bill type, then draws
/// biller-relevant line items from that vertical's catalog. Selection, amounts, payer, and due
/// dates are driven by a stable hash of the biller's identity, so:
///   * the same biller always yields the same set (reproducible, no uncurated RNG), and
///   * two different billers yield different sets (a per-biller reference is woven into each
///     line's description, so even two same-vertical billers never match).
/// This replaces the FR-6 defect where <c>bill_type</c> "insurance"/"other" returned a fixed,
/// hand-authored set (HOA dues, a pool special assessment, a Christmas-fine joke) regardless of
/// who the biller was.
/// </summary>
public sealed class DeterministicSeedInvoiceGenerator : ISeedInvoiceGenerator
{
    public string Provider => "deterministic";

    private const int MinCount = 1;
    private const int MaxCount = 24;

    private static readonly string[] PayerNames =
    [
        "Alex Rivera", "Jordan Chen", "Sam Okafor", "Priya Nair",
        "Morgan Lee", "Diego Santos", "Fatima Hassan", "Taylor Brooks",
    ];

    public IReadOnlyList<SeedInvoiceSpec> Generate(SeedBillerContext biller, int count)
    {
        ArgumentNullException.ThrowIfNull(biller);

        // When onboarding captured billing categories, seed at least one invoice per category so a
        // multi-category biller gets multiple, category-labelled invoices.
        if (biller.Categories.Count > 0)
        {
            return GenerateForCategories(biller, count);
        }

        var n = Math.Clamp(count, MinCount, MaxCount);

        var theme = ClassifyTheme(biller);
        var items = theme.Items;

        // A stable 64-bit seed over the biller's identity: reproducible per biller, distinct across
        // billers (biller id alone is unique, but name/website are folded in so the content — not
        // just an opaque number — reflects the specific biller).
        var seed = StableHash($"{biller.BillerId}|{Normalize(biller.Name)}|{biller.Website?.Host ?? string.Empty}");
        var rng = new SplitMix64(seed);

        // A short, biller-specific reference token woven into every line's description. This is what
        // guarantees two distinct billers never produce byte-identical seeded sets, even when they
        // land in the same vertical (the payer-visible text — description/type/note — differs).
        var reference = Base36(seed, 5);

        var start = (int)(rng.Next() % (ulong)items.Length);
        var specs = new List<SeedInvoiceSpec>(n);
        for (var i = 0; i < n; i++)
        {
            var item = items[(start + i) % items.Length];
            // Jitter the catalog base amount deterministically so amounts vary per biller too.
            var jitter = (int)(rng.Next() % 12) * 100;
            var amount = item.BaseAmountCents + jitter;
            var payer = PayerNames[(int)(rng.Next() % (ulong)PayerNames.Length)];
            // First bill reads as "due soon" (a natural demo highlight); the rest spread out weekly.
            var statusColor = i == 0 ? "yellow" : "green";

            specs.Add(new SeedInvoiceSpec(
                Description: $"{item.Description} (ref {reference}-{i + 1})",
                AmountCents: amount,
                DueInDays: 14 + (i * 7),
                PayerName: payer,
                Type: item.Type,
                StatusColor: statusColor));
        }

        return specs;
    }

    private static List<SeedInvoiceSpec> GenerateForCategories(SeedBillerContext biller, int count)
    {
        var categories = biller.Categories;
        // Guarantee coverage: at least one invoice per category, honoring the caller's count when it
        // asks for more (extra invoices wrap back over the categories as later occurrences).
        var n = Math.Clamp(Math.Max(count, categories.Count), MinCount, MaxCount);

        var seed = StableHash($"{biller.BillerId}|{Normalize(biller.Name)}|{biller.Website?.Host ?? string.Empty}");
        var rng = new SplitMix64(seed);
        var reference = Base36(seed, 5);

        var specs = new List<SeedInvoiceSpec>(n);
        for (var i = 0; i < n; i++)
        {
            var category = categories[i % categories.Count];
            var occurrence = (i / categories.Count) + 1;

            // Amount is stable per category (a hash of the category), then jittered per biller so two
            // billers with the same category still differ. Kept in a realistic demo range.
            var categorySeed = StableHash($"{category.Id}|{reference}");
            var baseAmount = 2500 + (int)(categorySeed % 90) * 100; // $25.00 – $114.00
            var amount = baseAmount + (int)(rng.Next() % 12) * 100;
            var payer = PayerNames[(int)(rng.Next() % (ulong)PayerNames.Length)];
            // First invoice reads as "due soon" (a natural demo highlight); the rest spread out.
            var statusColor = i == 0 ? "yellow" : "green";
            var description = occurrence == 1
                ? $"{category.DisplayName} (ref {reference}-{i + 1})"
                : $"{category.DisplayName} #{occurrence} (ref {reference}-{i + 1})";

            specs.Add(new SeedInvoiceSpec(
                Description: description,
                AmountCents: amount,
                DueInDays: DueInDaysForCadence(category.Cadence, i),
                PayerName: payer,
                Type: category.DisplayName,
                StatusColor: statusColor));
        }

        return specs;
    }

    // Cadence nudges the demo due dates so a monthly line reads sooner than a quarterly/annual one,
    // with a weekly stagger so multiple seeded invoices don't all land on the same day.
    private static int DueInDaysForCadence(string? cadence, int index)
    {
        var baseOffset = (cadence ?? string.Empty).ToLowerInvariant() switch
        {
            "onetime" or "one_time" or "adhoc" or "ad_hoc" => 10,
            "monthly" => 14,
            "quarterly" => 21,
            "annual" => 30,
            _ => 14,
        };

        return baseOffset + (index * 7);
    }

    private static SeedTheme ClassifyTheme(SeedBillerContext biller)
    {
        var haystack = string.Join(
            ' ',
            Normalize(biller.Name),
            Normalize(biller.BillType),
            Normalize(biller.Website?.Host ?? string.Empty));

        SeedTheme? best = null;
        var bestScore = 0;
        foreach (var theme in Themes)
        {
            var score = theme.Keywords.Count(keyword => ContainsWord(haystack, keyword));
            if (score > bestScore)
            {
                bestScore = score;
                best = theme;
            }
        }

        return best ?? GenericTheme;
    }

    private static bool ContainsWord(string haystack, string keyword) =>
        haystack.Contains(keyword, StringComparison.Ordinal);

    private static string Normalize(string? value) => (value ?? string.Empty).ToLowerInvariant();

    // ---- catalogs ---------------------------------------------------------------------------

    private sealed record SeedItem(string Type, string Description, int BaseAmountCents);

    private sealed record SeedTheme(string Id, string[] Keywords, SeedItem[] Items);

    private static readonly SeedTheme GenericTheme = new(
        "services",
        [],
        [
            new("Statement", "Monthly statement", 5900),
            new("Service", "Service charge", 3200),
            new("Balance", "Account balance", 8400),
            new("Subscription", "Recurring subscription", 2500),
            new("Usage", "Usage charge", 4600),
            new("Fee", "Processing fee", 1500),
        ]);

    private static readonly SeedTheme[] Themes =
    [
        new(
            "retail",
            ["apparel", "clothing", "clothes", "pants", "wear", "fashion", "boutique", "outfit",
             "threads", "shop", "store", "retail", "shoe", "footwear", "jeans", "merch"],
            [
                new("Order", "Online apparel order", 6800),
                new("Order", "In-store purchase", 4200),
                new("Shipping", "Return shipping fee", 1200),
                new("Membership", "Loyalty membership renewal", 2500),
                new("Store Credit", "Store credit adjustment", 3000),
                new("Order", "Seasonal collection preorder", 9500),
            ]),
        new(
            "civic",
            ["park", "parks", "recreation", "district", "community", "civic", "city", "county",
             "municipal", "township", "library", "permit"],
            [
                new("Permit", "Facility rental permit", 7500),
                new("Program", "Recreation program registration", 4500),
                new("Permit", "Park event permit", 3000),
                new("Services", "Community services fee", 5500),
                new("Fee", "Late equipment return fee", 1500),
                new("Pass", "Seasonal pool pass", 6000),
            ]),
        new(
            "insurance",
            ["insurance", "insure", "policy", "assurance", "coverage", "underwriting"],
            [
                new("Auto", "Auto policy premium", 14250),
                new("Home", "Homeowners policy premium", 8900),
                new("Life", "Life policy premium", 4500),
                new("Renters", "Renters policy premium", 2600),
                new("Umbrella", "Umbrella policy premium", 5200),
                new("Auto", "Policy installment", 7100),
            ]),
        new(
            "utility",
            ["utility", "utilities", "water", "sewer", "power", "electric", "electricity",
             "energy", "gas", "waste", "stormwater"],
            [
                new("Water", "Water & sewer service", 6400),
                new("Electric", "Electricity usage", 8800),
                new("Waste", "Waste collection", 3200),
                new("Stormwater", "Stormwater fee", 1500),
                new("Gas", "Natural gas service", 5400),
                new("Water", "Meter service charge", 2100),
            ]),
        new(
            "tax",
            ["tax", "taxes", "assessor", "treasurer", "property", "parcel", "levy"],
            [
                new("Property Tax", "Property tax installment", 42000),
                new("Assessment", "Assessment adjustment", 3800),
                new("Fee", "Late parcel fee", 2500),
                new("Levy", "Supplemental levy", 6900),
                new("Property Tax", "Second-half property tax", 41000),
                new("Fee", "Parcel processing fee", 1200),
            ]),
        new(
            "membership",
            ["gym", "fitness", "club", "studio", "yoga", "pilates", "wellness", "spa", "athletic"],
            [
                new("Membership", "Monthly membership dues", 5900),
                new("Class", "Group class package", 4400),
                new("Training", "Personal training sessions", 12000),
                new("Fee", "Locker rental", 1500),
                new("Membership", "Annual membership renewal", 39900),
                new("Class", "Drop-in class fee", 2200),
            ]),
        new(
            "healthcare",
            ["clinic", "dental", "dentist", "medical", "health", "veterinary", "hospital",
             "therapy", "pediatric", "orthodontic"],
            [
                new("Visit", "Office visit copay", 3500),
                new("Lab", "Lab work", 6200),
                new("Procedure", "Procedure balance", 18000),
                new("Prescription", "Prescription copay", 1800),
                new("Visit", "Follow-up visit", 2800),
                new("Statement", "Statement balance", 9400),
            ]),
        new(
            "education",
            ["school", "academy", "tuition", "university", "college", "learning", "education",
             "institute", "campus", "preschool", "daycare"],
            [
                new("Tuition", "Tuition installment", 45000),
                new("Fee", "Registration fee", 5000),
                new("Materials", "Course materials", 3200),
                new("Activity", "Activity fee", 2500),
                new("Fee", "Lab & technology fee", 4200),
                new("Housing", "Housing deposit", 30000),
            ]),
    ];

    // ---- stable hashing / prng --------------------------------------------------------------

    // FNV-1a (64-bit). Process-stable (unlike string.GetHashCode) so seeds reproduce across runs.
    private static ulong StableHash(string value)
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

    private static string Base36(ulong value, int length)
    {
        const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        Span<char> buffer = stackalloc char[length];
        for (var i = length - 1; i >= 0; i--)
        {
            buffer[i] = alphabet[(int)(value % 36)];
            value /= 36;
        }

        return new string(buffer);
    }

    /// <summary>Minimal deterministic PRNG (SplitMix64) — stable, seedable, dependency-free.</summary>
    private sealed class SplitMix64(ulong seed)
    {
        private ulong _state = seed;

        public ulong Next()
        {
            _state += 0x9E3779B97F4A7C15;
            var z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
            return z ^ (z >> 31);
        }
    }
}
