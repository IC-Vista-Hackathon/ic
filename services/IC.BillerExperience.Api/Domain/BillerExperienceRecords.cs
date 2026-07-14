using System.Text.Json.Serialization;
using IC.BillerExperience.Contracts.V1.Billers;
using IC.BillerExperience.Contracts.V1.Experiences;
using IC.BillerExperience.Contracts.V1.Onboarding;

namespace IC.BillerExperience.Api.Domain;

public sealed record BillerRecord(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("bill_type")] string BillType,
    [property: JsonPropertyName("postal_code")] string PostalCode,
    [property: JsonPropertyName("website")] Uri? Website,
    [property: JsonPropertyName("brand")] BillerBrand? Brand,
    [property: JsonPropertyName("support")] BillerSupport? Support,
    [property: JsonPropertyName("payment_rails")] IReadOnlyList<PaymentRailReference> PaymentRails,
    [property: JsonPropertyName("status")] BillerStatus Status,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

public sealed record ExperienceRecord(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("biller_id")] string BillerId,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("status")] ExperienceRevisionState State,
    [property: JsonPropertyName("definition")] BillerExperienceDefinition Definition,
    [property: JsonPropertyName("findings")] IReadOnlyList<ComplianceFinding> Findings,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("approved_at")] DateTimeOffset? ApprovedAt = null,
    [property: JsonIgnore] string? ETag = null);

public sealed record OnboardingRunRecord(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("biller_id")] string BillerId,
    [property: JsonPropertyName("workflow")] string Workflow,
    [property: JsonPropertyName("state")] OnboardingSessionState State,
    [property: JsonPropertyName("step")] int Step,
    [property: JsonPropertyName("messages")] IReadOnlyList<OnboardingChatMessage> Messages,
    [property: JsonPropertyName("missing_fields")] IReadOnlyList<string> MissingFields,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
    [property: JsonIgnore] string? ETag = null);

public sealed record DeploymentRecord(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("biller_id")] string BillerId,
    [property: JsonPropertyName("config_version")] int ConfigVersion,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("requested_at")] DateTimeOffset RequestedAt,
    [property: JsonIgnore] string? ETag = null);
