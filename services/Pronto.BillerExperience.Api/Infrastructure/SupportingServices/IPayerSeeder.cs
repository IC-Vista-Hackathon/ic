namespace Pronto.BillerExperience.Api.Infrastructure.SupportingServices;

/// <summary>
/// Seeds a deterministic demo payer into the PayerAccount service for a biller, linked to the
/// account number(s) its invoices were seeded under. Together with <see cref="IInvoiceSeeder"/>
/// this makes a newly provisioned biller's live payer site resolve real SERVICE data (invoice +
/// payer) for the demo account, not client-side sample data. Re-seeding must be idempotent.
/// </summary>
public interface IPayerSeeder
{
    ValueTask SeedAsync(
        SeedBillerContext biller,
        IReadOnlyList<string> accountNumbers,
        CancellationToken cancellationToken);
}

/// <summary>No-op used when the PayerAccount base URL is not configured (local/offline runs).</summary>
public sealed class NullPayerSeeder : IPayerSeeder
{
    public ValueTask SeedAsync(
        SeedBillerContext biller,
        IReadOnlyList<string> accountNumbers,
        CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
