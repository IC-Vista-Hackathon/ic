namespace IC.BillerExperience.Contracts.V1.Experiences;

public sealed record BillerExperienceDefinition(
    string SchemaVersion,
    string BillerId,
    ExperienceBrand Brand,
    ExperienceContent Content,
    PwaConfiguration Pwa,
    IReadOnlyList<string> EnabledPaymentCapabilities);

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
