namespace Pronto.BillerExperience.Api.Infrastructure.SupportingServices;

/// <summary>
/// A caller-supplied demo payer for onboarding seeding. The Biller Experience side (which owns
/// onboarding and the biller profile) chooses a biller-relevant demo identity; the deterministic
/// PayerAccount service stamps a payer id and persists it. Mirrors <see cref="SeedInvoiceSpec"/>
/// for the payer half of the seed path.
/// </summary>
public sealed record SeedPayerSpec(
    string Name,
    string Email,
    string? Phone,
    bool Autopay,
    bool Paperless,
    IReadOnlyList<string> Channels,
    int? PaymentDay);

/// <summary>
/// Chooses the deterministic demo payer for onboarding seeding (the "agent configures" half of the
/// payer seed path). The seam mirrors <see cref="ISeedInvoiceGenerator"/> — a deterministic
/// implementation ships today and an Azure OpenAI-backed one can slot in behind the same interface
/// without changing callers.
/// </summary>
public interface ISeedPayerGenerator
{
    /// <summary>How the payer was produced (surfaced for observability), e.g. "deterministic".</summary>
    string Provider { get; }

    /// <summary>
    /// Produce a deterministic demo payer for the biller. The result must be stable for a given
    /// biller (reproducible, so re-seeding is idempotent) yet distinct across different billers.
    /// </summary>
    SeedPayerSpec Generate(SeedBillerContext biller);
}
