using System.Text.Json.Serialization;

namespace Pronto.PayerExperience.Router;

// Shape of billers/{slug}/active.json written last by the publish step.
public sealed record ActiveRevision(
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("revision")] string Revision,
    [property: JsonPropertyName("site_prefix")] string? SitePrefix = null,
    [property: JsonPropertyName("entry")] string? Entry = null)
{
    // Older/config-only pointers may omit site_prefix; derive it from the revision.
    public string ResolveSitePrefix() =>
        string.IsNullOrWhiteSpace(SitePrefix)
            ? $"billers/{Slug}/revisions/{Revision}/site"
            : SitePrefix!;
}
