using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using IC.BillerExperience.Contracts.V1.Deployments;

namespace IC.BillerExperience.Api.Infrastructure.Publication;

public sealed partial class BlobPublishedExperienceStore(
    BlobContainerClient container,
    ILogger<BlobPublishedExperienceStore> logger) : IPublishedExperienceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public async ValueTask<PublishedExperienceArtifact?> GetActiveAsync(string slug, CancellationToken cancellationToken)
    {
        var blobName = $"billers/{slug}/active.json";
        using var activity = BillerExperienceTelemetry.Source.StartActivity("published_experience.read");
        activity?.SetTag("ic.biller_slug", slug);
        try
        {
            var response = await container.GetBlobClient(blobName).DownloadContentAsync(cancellationToken);
            return response.Value.Content.ToObjectFromJson<PublishedExperienceArtifact>(JsonOptions);
        }
        catch (RequestFailedException exception) when (exception.Status == (int)HttpStatusCode.NotFound)
        {
            LogArtifactNotFound(logger, slug, blobName, activity?.TraceId.ToString());
            return null;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            LogArtifactReadError(logger, slug, blobName, activity?.TraceId.ToString(), exception);
            throw;
        }
    }

    public async ValueTask<BinaryData?> GetManifestAsync(string slug, string revision, CancellationToken cancellationToken)
    {
        var blobName = $"billers/{slug}/revisions/{revision}/manifest.webmanifest";
        using var activity = BillerExperienceTelemetry.Source.StartActivity("published_experience.manifest.read");
        activity?.SetTag("ic.biller_slug", slug);
        activity?.SetTag("ic.revision", revision);
        try
        {
            var response = await container.GetBlobClient(blobName).DownloadContentAsync(cancellationToken);
            return response.Value.Content;
        }
        catch (RequestFailedException exception) when (exception.Status == (int)HttpStatusCode.NotFound)
        {
            LogArtifactNotFound(logger, slug, blobName, activity?.TraceId.ToString());
            return null;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            LogArtifactReadError(logger, slug, blobName, activity?.TraceId.ToString(), exception);
            throw;
        }
    }

    [LoggerMessage(2300, LogLevel.Warning, "Published artifact {BlobName} was not found for slug {Slug}; trace {TraceId}")]
    private static partial void LogArtifactNotFound(ILogger logger, string slug, string blobName, string? traceId);

    [LoggerMessage(2399, LogLevel.Error, "Reading published artifact {BlobName} failed for slug {Slug}; trace {TraceId}")]
    private static partial void LogArtifactReadError(ILogger logger, string slug, string blobName, string? traceId, Exception exception);
}
