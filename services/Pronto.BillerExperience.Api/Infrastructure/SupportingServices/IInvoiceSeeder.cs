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
    ValueTask SeedAsync(SeedBillerContext biller, CancellationToken cancellationToken);
}

public sealed class NullInvoiceSeeder : IInvoiceSeeder
{
    public ValueTask SeedAsync(SeedBillerContext biller, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
