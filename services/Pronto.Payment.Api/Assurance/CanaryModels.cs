namespace Pronto.Payment.Api.Assurance;

/// <summary>
/// A single synthetic canary target: the published biller, the pre-seeded canary invoice to pay,
/// the method to exercise, and the stable idempotency key that makes re-running the canary an
/// exactly-once replay rather than a second payment. <see cref="PayerAccountId"/> is optional
/// (guest pay) — a canary payer account when the biller requires one.
/// </summary>
public sealed record CanaryTarget(
    string BillerId,
    string InvoiceId,
    string Method,
    string IdempotencyKey,
    string? PayerAccountId = null);

/// <summary>Outcome of running one canary target through the genuine payment path.</summary>
public sealed record CanaryOutcome(
    string BillerId,
    string InvoiceId,
    string Method,
    bool Settled,
    bool IdempotentReplay,
    string? PaymentId,
    string? Confirmation,
    int AmountCents,
    int FeeCents,
    int TotalCents,
    string? FailureCode = null,
    string? FailureDetail = null);

/// <summary>Aggregate result of a canary run over all targets.</summary>
public sealed record CanaryRunResult(
    bool Ok,
    int TargetCount,
    IReadOnlyList<CanaryOutcome> Outcomes);
