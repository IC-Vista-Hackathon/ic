namespace Pronto.BillerExperience.Api.Infrastructure.SupportingServices;

/// <summary>
/// The biller context an onboarding seed needs to choose relevant demo invoices. This is the
/// biller identity known at creation time (before any discovery chat) — enough to seed
/// biller-specific, non-templated demo data per FR-6.
/// </summary>
public sealed record SeedBillerContext(
    string BillerId,
    string Name,
    string BillType,
    Uri? Website);

public interface IInvoiceSeeder
{
    /// <summary>
    /// Seed demo invoices for <paramref name="biller"/>. When <paramref name="replace"/> is true the
    /// existing invoices are replaced rather than appended (used by the resettable preview tenant so
    /// a re-seed is deterministic); the Invoice service only honors replace for preview partitions.
    /// </summary>
    ValueTask SeedAsync(SeedBillerContext biller, CancellationToken cancellationToken, bool replace = false);
}

public sealed class NullInvoiceSeeder : IInvoiceSeeder
{
    public ValueTask SeedAsync(SeedBillerContext biller, CancellationToken cancellationToken, bool replace = false) => ValueTask.CompletedTask;
}
