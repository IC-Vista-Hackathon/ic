using System.Text.Json.Serialization;
using Pronto.BillerExperience.Contracts.V1.Billing;

namespace Pronto.BillerExperience.Contracts.V1.Experiences;

public sealed record BillerExperienceDefinition(
    string SchemaVersion,
    string BillerId,
    ExperienceBrand Brand,
    ExperienceContent Content,
    PwaConfiguration Pwa,
    IReadOnlyList<string> EnabledPaymentCapabilities,
    ExperienceUi? Ui = null,
    ExperiencePreferences? Preferences = null,
    DesignBrief? Brief = null,
    BillingPresentation? Billing = null,
    TelemetryPolicy? Telemetry = null);

/// <summary>
/// Declarative posture for what the published experience is allowed to emit as browser telemetry.
/// The payer PWA structurally excludes PII (see its <c>telemetryPolicy.ts</c>); this mirrors that
/// intent into the config so the deterministic compliance suite can certify, per revision, that no
/// PII field was configured for capture. Absent/empty means "no custom telemetry capture", which is
/// the safe default.
/// </summary>
public sealed record TelemetryPolicy(
    bool BrowserTelemetryEnabled = false,
    IReadOnlyList<string>? CapturedFields = null);

/// <summary>
/// Public-safe projection of the server-owned billing policy. It contains only the details
/// needed to explain the biller's experience; operational services remain the authority for
/// invoice state transitions and payment eligibility.
/// </summary>
public sealed record BillingPresentation(IReadOnlyList<BillingPresentationCategory> Categories);

public sealed record BillingPresentationCategory(
    string Id,
    string DisplayName,
    BillingCadenceKind? Cadence,
    string CadenceLabel,
    string StateSummary,
    SettlementMode? PaymentMode,
    int? MaximumInstallments = null);

// The bounded creative input the bespoke-skin generator (Claude Opus) is allowed to
// author against. Deliberately separate from the functional contract
// (EnabledPaymentCapabilities/Preferences), which the generated code must honor but
// never change. Optional so existing revisions and the draft generator stay valid.
public sealed record DesignBrief(
    string VoiceAndTone,
    string VisualStyle,
    IReadOnlyList<string> BrandKeywords,
    IReadOnlyList<BrandAsset> Assets,
    Uri? ReferenceUrl = null,
    string? LayoutIntent = null);

public sealed record BrandAsset(string Kind, Uri Url, string? Description = null);

public sealed record ExperiencePreferences(
    bool GuestCheckoutAllowed,
    bool OfferAutopay,
    bool EnrollDuringPayment,
    bool OfferPaperless,
    ReminderChannel ReminderChannel,
    IReadOnlyList<string> AcceptedMethods,
    bool SelfServiceHistory,
    bool SelfServiceUpdates,
    FeeHandling FeeHandling,
    PreviewPreferences Preview,
    IReadOnlyDictionary<string, string>? RecommendationRationale = null);

public sealed record PreviewPreferences(string DefaultDevice, IReadOnlyList<string> EnabledScenarios);

public enum ReminderChannel
{
    Email,
    Text,
    Both,
    None
}

public enum FeeHandling
{
    Absorb,
    Charge,
    Mixed,
    Undecided
}

public sealed record ExperienceUi(
    string Layout,
    ExperienceTheme Theme,
    IReadOnlyList<ExperienceSection> Sections,
    IReadOnlyList<ExperienceAction> Actions);

public sealed record ExperienceTheme(string Density, string Radius, string Surface);

public sealed record ExperienceSection(string Id, string Type, string Variant = "default", bool Visible = true);

public sealed record ExperienceAction(
    string Id,
    string Label,
    ExperienceActionType Action,
    string Variant = "primary");

public enum ExperienceActionType
{
    StartPayment,
    SchedulePayment,
    ViewBill,
    ContactSupport
}

public sealed record UpdateExperienceRequest(
    BillerExperienceDefinition Definition,
    [property: JsonPropertyName("expected_etag")] string? ExpectedETag);

public sealed record ExperienceBrand(
    string DisplayName,
    string PrimaryColor,
    string SecondaryColor,
    string? LogoAssetId,
    string? FontFamily);

public sealed record ExperienceContent(
    string Heading,
    string Introduction,
    string SupportText,
    Uri PrivacyPolicyUrl,
    Uri TermsOfServiceUrl,
    Uri? RefundPolicyUrl = null,
    string? FeeDisclosure = null);

public sealed record PwaConfiguration(
    string Name,
    string ShortName,
    string ThemeColor,
    string BackgroundColor,
    string? IconAssetId);

public sealed record ApproveExperienceRequest(string Revision, string ApprovedBy);

public sealed record ComplianceFinding(
    string Code,
    string Message,
    ComplianceFindingSeverity Severity,
    bool RequiresReview = true,
    string? FieldPath = null,
    string? Jurisdiction = null,
    IReadOnlyList<Uri>? Sources = null,
    string? PolicyVersion = null);

public enum ComplianceFindingSeverity
{
    Information,
    Warning,
    Blocking
}

public sealed record ExperienceRevisionResponse(
    string BillerId,
    string Revision,
    BillerExperienceDefinition Definition,
    ExperienceRevisionState State,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ApprovedAt,
    string? ETag = null,
    IReadOnlyList<ComplianceFinding>? Findings = null);

public enum ExperienceRevisionState
{
    Draft,
    Approved,
    Publishing,
    Published,
    Superseded,
    Failed
}
