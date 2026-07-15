namespace Pronto.Payment.Api.Tests;

/// <summary>Minimal controllable <see cref="TimeProvider"/> so time-dependent behavior
/// (schedule-date validation, lease expiry, stale-pending recovery) is deterministic.</summary>
public sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset now = now;

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan delta) => now += delta;

    public void Set(DateTimeOffset value) => now = value;
}
