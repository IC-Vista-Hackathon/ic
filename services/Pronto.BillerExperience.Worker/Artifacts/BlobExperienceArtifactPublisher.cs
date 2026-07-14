using System.Diagnostics;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Pronto.BillerExperience.Contracts.V1.Deployments;

namespace Pronto.BillerExperience.Worker.Artifacts;

public sealed partial class BlobExperienceArtifactPublisher(
    BlobContainerClient container,
    ILogger<BlobExperienceArtifactPublisher> logger) : IExperienceArtifactPublisher
{
    public async ValueTask PublishAsync(PublicationArtifactPlan plan, CancellationToken cancellationToken)
    {
        using var activity = PublicationTelemetry.Source.StartActivity("publication.blob.publish");
        activity?.SetTag("ic.biller_id", plan.BillerId);
        activity?.SetTag("ic.revision", plan.Revision);
        try
        {
            await UploadAsync(
                $"{plan.RevisionPrefix}/config.json",
                plan.ConfigJson,
                "application/json",
                "public, max-age=31536000, immutable",
                plan,
                cancellationToken);
            await UploadAsync(
                $"{plan.RevisionPrefix}/manifest.webmanifest",
                plan.ManifestJson,
                "application/manifest+json",
                "public, max-age=31536000, immutable",
                plan,
                cancellationToken);

            // A single blob overwrite is atomic. Publishing the active artifact last makes the
            // versioned files visible before readers can observe the new revision.
            await UploadAsync(
                plan.ActiveBlobName,
                plan.ConfigJson,
                "application/json",
                "no-cache, no-store, must-revalidate",
                plan,
                cancellationToken);

            var verification = await container.GetBlobClient(plan.ActiveBlobName).DownloadContentAsync(cancellationToken);
            var active = verification.Value.Content.ToObjectFromJson<PublishedExperienceArtifact>(PublicationArtifactPlanFactory.JsonOptions);
            if (!string.Equals(active?.Revision, plan.Revision, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Active artifact verification failed for biller '{plan.BillerId}'.");
            }
            PublicationTelemetry.ArtifactsUploaded.Add(3);
            LogArtifactsPublished(logger, plan.BillerId, plan.Revision, plan.ActiveBlobName, activity?.TraceId.ToString());
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            LogArtifactError(logger, plan.BillerId, plan.Revision, activity?.TraceId.ToString(), exception);
            throw;
        }
    }

    private async ValueTask UploadAsync(
        string blobName,
        string content,
        string contentType,
        string cacheControl,
        PublicationArtifactPlan plan,
        CancellationToken cancellationToken)
    {
        var options = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType, CacheControl = cacheControl },
            Metadata = new Dictionary<string, string>
            {
                ["biller_id"] = plan.BillerId,
                ["revision"] = plan.Revision
            }
        };
        await container.GetBlobClient(blobName).UploadAsync(BinaryData.FromString(content), options, cancellationToken);
        LogArtifactUploaded(logger, plan.BillerId, plan.Revision, blobName);
    }

    [LoggerMessage(1200, LogLevel.Information, "Uploaded artifact {BlobName} for biller {BillerId}, revision {Revision}")]
    private static partial void LogArtifactUploaded(ILogger logger, string billerId, string revision, string blobName);

    [LoggerMessage(1201, LogLevel.Information, "Published artifacts for biller {BillerId}, revision {Revision}; active blob {ActiveBlobName}; trace {TraceId}")]
    private static partial void LogArtifactsPublished(ILogger logger, string billerId, string revision, string activeBlobName, string? traceId);

    [LoggerMessage(1901, LogLevel.Error, "Artifact publication failed for biller {BillerId}, revision {Revision}; trace {TraceId}")]
    private static partial void LogArtifactError(ILogger logger, string billerId, string revision, string? traceId, Exception exception);
}
