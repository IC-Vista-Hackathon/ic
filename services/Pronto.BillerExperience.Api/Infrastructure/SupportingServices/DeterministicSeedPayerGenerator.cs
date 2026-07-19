namespace Pronto.BillerExperience.Api.Infrastructure.SupportingServices;

/// <summary>
/// Deterministic implementation of <see cref="ISeedPayerGenerator"/>. It derives a single demo
/// payer from a stable hash of the biller's identity, so:
///   * the same biller always yields the same payer (reproducible — re-seeding is idempotent), and
///   * two different billers yield different payers (name + email reference differ).
/// The payer opts into email notifications with autopay/paperless off, matching the conservative
/// defaults a real payer would start from; the demo can toggle preferences from there.
/// </summary>
public sealed class DeterministicSeedPayerGenerator : ISeedPayerGenerator
{
    public string Provider => "deterministic";

    // A stable roster the demo payer is drawn from. Kept in sync with the invoice generator's payer
    // names so a seeded biller's demo payer reads like the person its invoices are addressed to.
    private static readonly string[] PayerNames =
    [
        "Alex Rivera", "Jordan Chen", "Sam Okafor", "Priya Nair",
        "Morgan Lee", "Diego Santos", "Fatima Hassan", "Taylor Brooks",
    ];

    public SeedPayerSpec Generate(SeedBillerContext biller)
    {
        ArgumentNullException.ThrowIfNull(biller);

        var seed = StableHash($"{biller.BillerId}|{Normalize(biller.Name)}|{biller.Website?.Host ?? string.Empty}");
        var name = PayerNames[(int)(seed % (ulong)PayerNames.Length)];
        // A short biller-specific reference keeps the demo email unique per biller (and stable), so
        // re-seeding the same biller collides on the PayerAccount service's per-biller email
        // uniqueness rule and is absorbed as an idempotent no-op rather than a second payer.
        var reference = Base36(seed, 6).ToLowerInvariant();

        return new SeedPayerSpec(
            Name: name,
            Email: $"demo.payer.{reference}@pronto-demo.example",
            Phone: null,
            Autopay: false,
            Paperless: false,
            Channels: ["email"],
            PaymentDay: null);
    }

    private static string Normalize(string? value) => (value ?? string.Empty).ToLowerInvariant();

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
}
