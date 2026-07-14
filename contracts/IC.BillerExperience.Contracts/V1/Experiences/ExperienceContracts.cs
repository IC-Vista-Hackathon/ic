namespace IC.BillerExperience.Contracts.V1.Experiences;

public sealed record BillerExperienceDefinition(
    string SchemaVersion,
    string BillerId,
    ExperienceBrand Brand,
    ExperienceContent Content,
    PwaConfiguration Pwa,
    IReadOnlyList<string> EnabledPaymentCapabilities,
    ExperienceUi? Ui = null);

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

public sealed record UpdateExperienceRequest(BillerExperienceDefinition Definition, string? ExpectedETag);

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
    Uri TermsOfServiceUrl);

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
    bool RequiresReview = true);

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
