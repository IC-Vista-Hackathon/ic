using Microsoft.Extensions.Options;

namespace Pronto.Payment.Api.Assurance;

/// <summary>
/// Supplies the set of published billers the canary should probe. A concrete source could enumerate
/// published billers from the Biller Experience API; the default reads a configured list, keeping
/// the assurance layer decoupled from control-plane wiring for the POC.
/// </summary>
public interface ICanaryTargetSource
{
    Task<IReadOnlyList<CanaryTarget>> GetTargetsAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Reads canary targets from the <c>Assurance:CanaryTargets</c> config section. Each entry names a
/// published biller plus the pre-seeded canary invoice/method to exercise. Empty by default.
/// </summary>
public sealed class ConfigurationCanaryTargetSource : ICanaryTargetSource
{
    private readonly IReadOnlyList<CanaryTarget> targets;

    public ConfigurationCanaryTargetSource(IOptions<CanaryTargetsOptions> options)
    {
        targets = options.Value.CanaryTargets
            .Where(target => !string.IsNullOrWhiteSpace(target.BillerId)
                && !string.IsNullOrWhiteSpace(target.InvoiceId)
                && !string.IsNullOrWhiteSpace(target.Method))
            .Select(target => new CanaryTarget(
                target.BillerId,
                target.InvoiceId,
                target.Method,
                string.IsNullOrWhiteSpace(target.IdempotencyKey)
                    ? $"canary:{target.BillerId}:{target.InvoiceId}"
                    : target.IdempotencyKey,
                target.PayerAccountId))
            .ToArray();
    }

    public Task<IReadOnlyList<CanaryTarget>> GetTargetsAsync(CancellationToken cancellationToken)
        => Task.FromResult(targets);
}

/// <summary>Config binding for <see cref="ConfigurationCanaryTargetSource"/>.</summary>
public sealed class CanaryTargetsOptions
{
    public const string SectionName = "Assurance";

    public IReadOnlyList<CanaryTargetConfig> CanaryTargets { get; set; } = [];
}

/// <summary>A single configured canary target (bound from config, so all fields are settable).</summary>
public sealed class CanaryTargetConfig
{
    public string BillerId { get; set; } = string.Empty;

    public string InvoiceId { get; set; } = string.Empty;

    public string Method { get; set; } = "card";

    public string? IdempotencyKey { get; set; }

    public string? PayerAccountId { get; set; }
}
