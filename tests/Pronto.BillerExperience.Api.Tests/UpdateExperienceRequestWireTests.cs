using System.Text.Json;
using System.Text.Json.Serialization;
using Pronto.BillerExperience.Contracts.V1.Experiences;
using Xunit;

namespace Pronto.BillerExperience.Api.Tests;

/// <summary>
/// Locks the optimistic-concurrency token's wire name. The Studio client sends
/// <c>expected_etag</c>; without the explicit JsonPropertyName the SnakeCaseLower policy
/// binds the property as <c>expected_e_tag</c>, silently dropping the token and defeating
/// the If-Match precondition on config updates.
/// </summary>
public sealed class UpdateExperienceRequestWireTests
{
    private static JsonSerializerOptions WireOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }

    private static BillerExperienceDefinition Definition() =>
        new(
            "1.1",
            "b-1",
            new ExperienceBrand("Acme", "#111111", "#222222", null, null),
            new ExperienceContent("h", "i", "s", new Uri("https://x/p"), new Uri("https://x/t")),
            new PwaConfiguration("n", "sn", "#333333", "#444444", null),
            ["card"]);

    [Fact]
    public void ExpectedETagSerializesAsExpectedEtag()
    {
        var json = JsonSerializer.Serialize(
            new UpdateExperienceRequest(Definition(), "etag-123"), WireOptions());

        Assert.Contains("\"expected_etag\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("expected_e_tag", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpectedEtagFromClientBodyBinds()
    {
        const string body = """
            {"definition":{"schema_version":"1.1","biller_id":"b-1",
            "brand":{"display_name":"Acme","primary_color":"#111111","secondary_color":"#222222"},
            "content":{"heading":"h","introduction":"i","support_text":"s","privacy_policy_url":"https://x/p","terms_of_service_url":"https://x/t"},
            "pwa":{"name":"n","short_name":"sn","theme_color":"#333333","background_color":"#444444"},
            "enabled_payment_capabilities":["card"]},"expected_etag":"etag-123"}
            """;

        var request = JsonSerializer.Deserialize<UpdateExperienceRequest>(body, WireOptions());

        Assert.NotNull(request);
        Assert.Equal("etag-123", request!.ExpectedETag);
    }
}
