using IC.BillerExperience.Contracts.V1.Billers;
using IC.BillerExperience.Contracts.V1.Experiences;
using IC.BillerExperience.Contracts.V1.Onboarding;
using Newtonsoft.Json;

namespace IC.BillerExperience.Api.Domain;

public sealed record BillerRecord(
    [property: JsonProperty("id")] string Id,
    [property: JsonProperty("name")] string Name,
    [property: JsonProperty("slug")] string Slug,
    [property: JsonProperty("bill_type")] string BillType,
    [property: JsonProperty("postal_code")] string PostalCode,
    [property: JsonProperty("website")] Uri? Website,
    [property: JsonProperty("brand")] BillerBrand? Brand,
    [property: JsonProperty("support")] BillerSupport? Support,
    [property: JsonProperty("payment_rails")] IReadOnlyList<PaymentRailReference> PaymentRails,
    [property: JsonProperty("status")] BillerStatus Status,
    [property: JsonProperty("created_at")] DateTimeOffset CreatedAt);

public sealed record ExperienceRecord(
    [property: JsonProperty("id")] string Id,
    [property: JsonProperty("biller_id")] string BillerId,
    [property: JsonProperty("version")] int Version,
    [property: JsonProperty("status")] ExperienceRevisionState State,
    [property: JsonProperty("definition")] BillerExperienceDefinition Definition,
    [property: JsonProperty("findings")] IReadOnlyList<ComplianceFinding> Findings,
    [property: JsonProperty("created_at")] DateTimeOffset CreatedAt,
    [property: JsonProperty("approved_at")] DateTimeOffset? ApprovedAt = null,
    [property: JsonIgnore] string? ETag = null);

public sealed record OnboardingRunRecord(
    [property: JsonProperty("id")] string Id,
    [property: JsonProperty("biller_id")] string BillerId,
    [property: JsonProperty("workflow")] string Workflow,
    [property: JsonProperty("state")] OnboardingSessionState State,
    [property: JsonProperty("step")] int Step,
    [property: JsonProperty("messages")] IReadOnlyList<OnboardingChatMessage> Messages,
    [property: JsonProperty("missing_fields")] IReadOnlyList<string> MissingFields,
    [property: JsonProperty("updated_at")] DateTimeOffset UpdatedAt,
    [property: JsonIgnore] string? ETag = null);

public sealed record DeploymentRecord(
    [property: JsonProperty("id")] string Id,
    [property: JsonProperty("biller_id")] string BillerId,
    [property: JsonProperty("config_version")] int ConfigVersion,
    [property: JsonProperty("status")] string Status,
    [property: JsonProperty("requested_at")] DateTimeOffset RequestedAt,
    [property: JsonProperty("updated_at")] DateTimeOffset? UpdatedAt = null,
    [property: JsonProperty("published_url")] Uri? PublishedUrl = null,
    [property: JsonProperty("failure_code")] string? FailureCode = null,
    [property: JsonProperty("failure_message")] string? FailureMessage = null,
    [property: JsonIgnore] string? ETag = null);
