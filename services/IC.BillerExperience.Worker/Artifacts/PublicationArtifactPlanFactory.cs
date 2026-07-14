using System.Text.Json;
using IC.BillerExperience.Contracts.V1.Deployments;
using IC.BillerExperience.Worker.Persistence;
using Microsoft.Extensions.Options;

namespace IC.BillerExperience.Worker.Artifacts;

public sealed class PublicationArtifactPlanFactory(IOptions<PublicationOptions> options)
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    public PublicationArtifactPlan Create(
        PublicationDeployment deployment,
        PublicationBiller biller,
        PublicationExperience experience)
    {
        var revision = $"config-{experience.Version}";
        var artifact = new PublishedExperienceArtifact(
            biller.Id,
            biller.Slug,
            revision,
            experience.Definition,
            deployment.RequestedAt);
        var routePrefix = $"pay/{biller.Slug}/";
        var publicRoot = new Uri(EnsureTrailingSlash(options.Value.PublicBaseUrl), UriKind.Absolute);
        var manifest = new
        {
            name = experience.Definition.Pwa.Name,
            short_name = experience.Definition.Pwa.ShortName,
            start_url = $"/{routePrefix}",
            scope = $"/{routePrefix}",
            display = "standalone",
            theme_color = experience.Definition.Pwa.ThemeColor,
            background_color = experience.Definition.Pwa.BackgroundColor
        };

        return new PublicationArtifactPlan(
            biller.Id,
            biller.Slug,
            revision,
            $"billers/{biller.Slug}/revisions/{revision}",
            $"billers/{biller.Slug}/active.json",
            JsonSerializer.Serialize(artifact, JsonOptions),
            JsonSerializer.Serialize(manifest, JsonOptions),
            new Uri(publicRoot, routePrefix),
            artifact);
    }

    private static string EnsureTrailingSlash(string value) => value.EndsWith('/') ? value : $"{value}/";
}
