using Pronto.BillerExperience.Contracts.V1.Experiences;
using Newtonsoft.Json;

namespace Pronto.BillerExperience.Worker.Persistence;

public sealed record PublicationDeployment(
    [property: JsonProperty("id")] string Id,
    [property: JsonProperty("biller_id")] string BillerId,
    [property: JsonProperty("config_version")] int ConfigVersion,
    [property: JsonProperty("status")] string Status,
    [property: JsonProperty("requested_at")] DateTimeOffset RequestedAt,
    [property: JsonProperty("updated_at")] DateTimeOffset? UpdatedAt = null,
    [property: JsonProperty("published_url")] Uri? PublishedUrl = null,
    [property: JsonProperty("failure_code")] string? FailureCode = null,
    [property: JsonProperty("failure_message")] string? FailureMessage = null,
    [property: JsonProperty("traceparent")] string? Traceparent = null,
    [property: JsonProperty("_etag")] string? ETag = null);

public sealed record PublicationBiller(
    [property: JsonProperty("id")] string Id,
    [property: JsonProperty("name")] string Name,
    [property: JsonProperty("slug")] string Slug);

public sealed record PublicationExperience(
    [property: JsonProperty("id")] string Id,
    [property: JsonProperty("biller_id")] string BillerId,
    [property: JsonProperty("version")] int Version,
    [property: JsonProperty("definition")] BillerExperienceDefinition Definition);

public static class PublicationStates
{
    public const string Requested = "requested";
    public const string Applying = "applying";
    public const string Ready = "ready";
    public const string Failed = "failed";
}
