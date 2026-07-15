namespace Pronto.BillerExperience.Contracts.V1.Billing;

/// <summary>
/// Biller-confirmed billing policy collected by the bounded onboarding conversation.
/// This is operational input, not visual experience configuration.
/// </summary>
public sealed record BillingProfile(
    string SchemaVersion,
    IReadOnlyList<BillingCategory> Categories,
    bool Confirmed = false)
{
    public static BillingProfile Empty { get; } = new("1.0", []);
}

public sealed record BillingCategory(
    string Id,
    string DisplayName,
    BillingCadence? Cadence = null,
    IReadOnlyList<AccountStateRule>? StateRules = null,
    PaymentTerms? PaymentTerms = null,
    bool Confirmed = false);

public sealed record BillingCadence(
    BillingCadenceKind Kind,
    string? Details = null);

public enum BillingCadenceKind
{
    Monthly,
    Quarterly,
    Annual,
    OneTime,
    AdHoc,
    Custom
}

/// <summary>
/// Declarative state policy. Supporting services remain responsible for validating and
/// executing transitions; agents must never turn this text into executable code.
/// </summary>
public sealed record AccountStateRule(
    string Description,
    int? GracePeriodDays = null,
    string? ResultingState = null);

public sealed record PaymentTerms(
    SettlementMode Mode,
    int? MaximumInstallments = null,
    string? Details = null,
    bool LimitsConfirmed = false);

public enum SettlementMode
{
    PayInFull,
    InstallmentsAllowed
}

public sealed record BillingDiscoveryQuestion(
    string QuestionId,
    BillingDiscoveryDimension Dimension,
    string Prompt,
    string? CategoryId = null,
    string? CategoryName = null,
    int Sequence = 0,
    string? ReasonCode = null);

public enum BillingDiscoveryDimension
{
    Categories,
    Cadence,
    StateRules,
    PaymentTerms,
    Confirmation
}

public sealed record BillingDiscoveryProgress(
    int Completed,
    int Total,
    bool IsComplete);

public sealed record ReopenBillingQuestionRequest(string QuestionId);

/// <summary>
/// A biller-authored answer captured by a guided UI. Dimension is explicit so the server can
/// reject reordered or stale answers instead of applying them to the wrong required question.
/// </summary>
public sealed record BillingDiscoveryAnswer(
    BillingDiscoveryDimension Dimension,
    string Answer);
