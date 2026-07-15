using System.Diagnostics;
using Azure;
using Azure.Storage;
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
            await UploadImmutableAsync(
                $"{plan.RevisionPrefix}/config.json",
                plan.ConfigJson,
                "application/json",
                "public, max-age=31536000, immutable",
                plan,
                cancellationToken);
            await UploadImmutableAsync(
                $"{plan.RevisionPrefix}/manifest.webmanifest",
                plan.ManifestJson,
                "application/manifest+json",
                "public, max-age=31536000, immutable",
                plan,
                cancellationToken);

            // A single blob overwrite is atomic. Publishing the active artifact last makes the
            // versioned files visible before readers can observe the new revision.
            await UploadActiveAsync(
                plan.ActiveBlobName,
                plan.ConfigJson,
                "application/json",
                "no-cache, no-store, must-revalidate",
                plan,
                cancellationToken);

            try
            {
                var verification = await container.GetBlobClient(plan.ActiveBlobName).DownloadContentAsync(cancellationToken);
                var active = verification.Value.Content.ToObjectFromJson<PublishedExperienceArtifact>(PublicationArtifactPlanFactory.JsonOptions);
                if (!string.Equals(active?.Revision, plan.Revision, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Active artifact verification failed for biller '{plan.BillerId}'.");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new ArtifactActivationException(
                    $"Could not verify active artifact for biller '{plan.BillerId}'.",
                    exception);
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

    private async ValueTask UploadImmutableAsync(
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
            },
            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
        };
        var blob = container.GetBlobClient(blobName);
        try
        {
            await blob.UploadAsync(BinaryData.FromString(content), options, cancellationToken);
        }
        catch (RequestFailedException exception)
            when (exception.Status == StatusCodes.Status409Conflict || exception.Status == StatusCodes.Status412PreconditionFailed)
        {
            var existing = await blob.DownloadContentAsync(cancellationToken);
            if (!string.Equals(existing.Value.Content.ToString(), content, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Immutable artifact '{blobName}' already exists with different content.");
            }
        }
        LogArtifactUploaded(logger, plan.BillerId, plan.Revision, blobName);
    }

    private async ValueTask UploadActiveAsync(
        string blobName,
        string content,
        string contentType,
        string cacheControl,
        PublicationArtifactPlan plan,
        CancellationToken cancellationToken)
    {
        var blob = container.GetBlobClient(blobName);
        var current = await ReadActiveAsync(blob, cancellationToken);
        if (current.Pointer is not null &&
            TryConfigVersion(current.Pointer.Revision, out var activeVersion) &&
            TryConfigVersion(plan.Revision, out var planVersion) &&
            activeVersion > planVersion)
        {
            throw new InvalidOperationException(
                $"Active revision '{current.Pointer.Revision}' is newer than attempted revision '{plan.Revision}'.");
        }

        var options = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType, CacheControl = cacheControl },
            Metadata = new Dictionary<string, string>
            {
                ["biller_id"] = plan.BillerId,
                ["revision"] = plan.Revision
            },
            Conditions = current.ETag.HasValue
                ? new BlobRequestConditions { IfMatch = current.ETag.Value }
                : new BlobRequestConditions { IfNoneMatch = ETag.All }
        };
        try
        {
            try
            {
                await blob.UploadAsync(BinaryData.FromString(content), options, cancellationToken);
            }
            catch (RequestFailedException exception)
                when (exception.Status == StatusCodes.Status409Conflict || exception.Status == StatusCodes.Status412PreconditionFailed)
            {
                var latest = await blob.DownloadContentAsync(cancellationToken);
                var latestPointer = latest.Value.Content.ToObjectFromJson<ActivePointer>(PublicationArtifactPlanFactory.JsonOptions);
                if (!string.Equals(latestPointer?.Revision, plan.Revision, StringComparison.Ordinal) ||
                    !string.Equals(latest.Value.Content.ToString(), content, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Active revision changed concurrently while publishing '{plan.Revision}'.",
                        exception);
                }
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (RequestFailedException exception)
        {
            throw new ArtifactActivationException(
                $"Active artifact update may have completed for biller '{plan.BillerId}'.",
                exception);
        }
        LogArtifactUploaded(logger, plan.BillerId, plan.Revision, blobName);
    }

    private static async ValueTask<(ActivePointer? Pointer, ETag? ETag)> ReadActiveAsync(
        BlobClient blob,
        CancellationToken cancellationToken)
    {
        try
        {
            var download = await blob.DownloadContentAsync(cancellationToken);
            var active = download.Value.Content.ToObjectFromJson<ActivePointer>(PublicationArtifactPlanFactory.JsonOptions);
            return (active, download.Value.Details.ETag);
        }
        catch (RequestFailedException exception) when (exception.Status == StatusCodes.Status404NotFound)
        {
            return (null, null);
        }
    }

    private static bool TryConfigVersion(string revision, out int version)
    {
        const string prefix = "config-";
        if (revision.StartsWith(prefix, StringComparison.Ordinal))
        {
            return int.TryParse(revision[prefix.Length..], out version);
        }

        version = 0;
        return false;
    }

    private sealed record ActivePointer(string Revision);

    [LoggerMessage(1200, LogLevel.Information, "Uploaded artifact {BlobName} for biller {BillerId}, revision {Revision}")]
    private static partial void LogArtifactUploaded(ILogger logger, string billerId, string revision, string blobName);

    [LoggerMessage(1201, LogLevel.Information, "Published artifacts for biller {BillerId}, revision {Revision}; active blob {ActiveBlobName}; trace {TraceId}")]
    private static partial void LogArtifactsPublished(ILogger logger, string billerId, string revision, string activeBlobName, string? traceId);

    [LoggerMessage(1901, LogLevel.Error, "Artifact publication failed for biller {BillerId}, revision {Revision}; trace {TraceId}")]
    private static partial void LogArtifactError(ILogger logger, string billerId, string revision, string? traceId, Exception exception);
}
