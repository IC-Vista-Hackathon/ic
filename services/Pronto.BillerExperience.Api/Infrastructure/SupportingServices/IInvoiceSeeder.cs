namespace Pronto.BillerExperience.Api.Infrastructure.SupportingServices;

/// <summary>
/// The biller context an onboarding seed needs to choose relevant demo invoices. This is the
/// biller identity known at creation time (before any discovery chat) — enough to seed
/// biller-specific, non-templated demo data per FR-6.
///
/// <see cref="Categories"/> carries what onboarding discovery captured (the biller's billing
/// categories with their cadence). When present, the generator produces at least one invoice per
/// category so a multi-category biller gets multiple, category-labelled invoices (setting up the
/// sibling cart feature). When empty (e.g. at creation, before discovery), the generator falls
/// back to its vertical-catalog behavior.
/// </summary>
public sealed record SeedBillerContext(
    string BillerId,
    string Name,
    string BillType,
    Uri? Website)
{
    /// <summary>Billing categories captured during onboarding; empty until discovery runs.</summary>
    public IReadOnlyList<SeedBillingCategory> Categories { get; init; } = [];
}

/// <summary>A billing category captured during onboarding, projected for invoice seeding.</summary>
public sealed record SeedBillingCategory(
    string Id,
    string DisplayName,
    string? Cadence);

public interface IInvoiceSeeder
{
    ValueTask SeedAsync(SeedBillerContext biller, CancellationToken cancellationToken);
}

public sealed class NullInvoiceSeeder : IInvoiceSeeder
{
    public ValueTask SeedAsync(SeedBillerContext biller, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
