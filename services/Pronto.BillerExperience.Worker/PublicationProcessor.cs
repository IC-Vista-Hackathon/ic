using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Pronto.BillerExperience.Worker.Artifacts;
using Pronto.BillerExperience.Worker.Building;
using Pronto.BillerExperience.Worker.Persistence;

namespace Pronto.BillerExperience.Worker;

public sealed partial class PublicationProcessor(
    IPublicationRepository repository,
    PublicationArtifactPlanFactory planFactory,
    IExperienceArtifactPublisher publisher,
    IExperienceBundleBuilder bundleBuilder,
    IOptions<PublicationOptions> publicationOptions,
    ILogger<PublicationProcessor> logger)
{
    private readonly PublicationOptions _options = publicationOptions.Value;

    public async ValueTask ProcessAsync(PublicationDeployment deployment, CancellationToken cancellationToken)
    {
        // Asynchronous publish is queued work: the originating API request's context is attached as
        // a link, not a parent, so the Worker's processing span is its own root trace (messaging
        // convention) while remaining correlatable to the request that enqueued it.
        var links = ActivityContext.TryParse(deployment.Traceparent, null, out var originating)
            ? new[] { new ActivityLink(originating) }
            : null;
        using var activity = PublicationTelemetry.Source.StartActivity(
            "publication.process",
            ActivityKind.Consumer,
            parentContext: default,
            links: links);
        activity?.SetTag("ic.biller_id", deployment.BillerId);
        activity?.SetTag("ic.deployment_id", deployment.Id);
        var startedAt = Stopwatch.GetTimestamp();
        var activated = false;
        try
        {
            LogPublicationStarted(logger, deployment.BillerId, deployment.Id, deployment.ConfigVersion, activity?.TraceId.ToString());
            var biller = await repository.GetBillerAsync(deployment.BillerId, cancellationToken);
            var experience = await repository.GetExperienceAsync(deployment.BillerId, deployment.ConfigVersion, cancellationToken);
            var plan = planFactory.Create(deployment, biller, experience);

            if (deployment.Status != PublicationStates.Verifying)
            {
                // Build + upload the bespoke static bundle first (no active.json), then let the config
                // publisher flip active.json last — so the site is fully uploaded before it's served.
                await BuildBundleAsync(plan, experience, cancellationToken);
                deployment = await repository.SaveAsync(deployment with
                {
                    Status = PublicationStates.Verifying,
                    UpdatedAt = DateTimeOffset.UtcNow
                }, cancellationToken);
            }
            await publisher.PublishAsync(plan, cancellationToken);
            activated = true;
            await CompleteActivatedPublicationAsync(deployment, plan.PublishedUrl, cancellationToken);

            PublicationTelemetry.Publications.Add(1, new KeyValuePair<string, object?>("outcome", "ready"));
            LogPublicationReady(logger, deployment.BillerId, deployment.Id, plan.PublishedUrl, activity?.TraceId.ToString());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogPublicationCancelled(logger, deployment.BillerId, deployment.Id, activity?.TraceId.ToString());
            if (activated)
            {
                await TryCompleteActivatedPublicationAsync(
                    deployment,
                    activity?.TraceId.ToString(),
                    ensureActive: false,
                    CancellationToken.None);
            }
            else
            {
                await TryReleaseClaimAsync(deployment, activity?.TraceId.ToString());
            }
            throw;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            var activationMayHaveSucceeded = activated || exception is ArtifactActivationException;
            if (activationMayHaveSucceeded)
            {
                PublicationTelemetry.Publications.Add(1, new KeyValuePair<string, object?>("outcome", "activation_finalization_failed"));
                LogActivatedPublicationFinalizationFailed(logger, deployment.BillerId, deployment.Id, activity?.TraceId.ToString(), exception);
                await TryCompleteActivatedPublicationAsync(
                    deployment,
                    activity?.TraceId.ToString(),
                    ensureActive: !activated,
                    cancellationToken);
                return;
            }

            PublicationTelemetry.Publications.Add(1, new KeyValuePair<string, object?>("outcome", "failed"));
            LogPublicationFailed(logger, deployment.BillerId, deployment.Id, activity?.TraceId.ToString(), exception);
            try
            {
                await repository.SaveAsync(deployment with
                {
                    Status = PublicationStates.Failed,
                    FailureCode = FailureCode(exception),
                    FailureMessage = SafeFailureMessage(exception),
                    UpdatedAt = DateTimeOffset.UtcNow,
                    ClaimedAt = null,
                    LeaseExpiresAt = null
                }, cancellationToken);
                await repository.MarkWorkflowAsync(deployment.BillerId, deployment.ConfigVersion, published: false, cancellationToken);
            }
            catch (Exception persistenceException)
            {
                LogFailureStatusError(logger, deployment.BillerId, deployment.Id, activity?.TraceId.ToString(), persistenceException);
            }
        }
        finally
        {
            PublicationTelemetry.PublicationDuration.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        }
    }

    private async ValueTask BuildBundleAsync(
        PublicationArtifactPlan plan,
        PublicationExperience experience,
        CancellationToken cancellationToken)
    {
        if (!bundleBuilder.Enabled)
        {
            throw new InvalidOperationException("BundleBuild:BuilderImage is required for router-backed publication.");
        }

        var definitionJson = JsonSerializer.Serialize(experience.Definition, PublicationArtifactPlanFactory.JsonOptions);
        await bundleBuilder.BuildAsync(
            new BundleBuildRequest(
                plan.BillerId,
                plan.Slug,
                plan.Revision,
                definitionJson,
                _options.StorageEndpoint,
                _options.ContainerName),
            cancellationToken);
    }

    private async ValueTask CompleteActivatedPublicationAsync(
        PublicationDeployment deployment,
        Uri publishedUrl,
        CancellationToken cancellationToken)
    {
        await repository.MarkWorkflowAsync(deployment.BillerId, deployment.ConfigVersion, published: true, cancellationToken);
        await repository.SaveAsync(deployment with
        {
            Status = PublicationStates.Ready,
            PublishedUrl = publishedUrl,
            UpdatedAt = DateTimeOffset.UtcNow,
            ClaimedAt = null,
            LeaseExpiresAt = null
        }, cancellationToken);
    }

    private async ValueTask TryCompleteActivatedPublicationAsync(
        PublicationDeployment deployment,
        string? traceId,
        bool ensureActive,
        CancellationToken cancellationToken)
    {
        try
        {
            var biller = await repository.GetBillerAsync(deployment.BillerId, cancellationToken);
            var experience = await repository.GetExperienceAsync(deployment.BillerId, deployment.ConfigVersion, cancellationToken);
            var plan = planFactory.Create(deployment, biller, experience);
            if (ensureActive)
            {
                await publisher.PublishAsync(plan, cancellationToken);
            }
            await CompleteActivatedPublicationAsync(deployment, plan.PublishedUrl, cancellationToken);
        }
        catch (Exception exception)
        {
            LogActivatedPublicationRepairFailed(logger, deployment.BillerId, deployment.Id, traceId, exception);
        }
    }

    private async ValueTask TryReleaseClaimAsync(PublicationDeployment deployment, string? traceId)
    {
        try
        {
            await repository.SaveAsync(deployment with
            {
                Status = deployment.Status == PublicationStates.Verifying
                    ? PublicationStates.Verifying
                    : PublicationStates.Requested,
                UpdatedAt = DateTimeOffset.UtcNow,
                ClaimedAt = null,
                LeaseExpiresAt = null
            }, CancellationToken.None);
        }
        catch (Exception exception)
        {
            LogClaimReleaseFailed(logger, deployment.BillerId, deployment.Id, traceId, exception);
        }
    }

    private static string FailureCode(Exception exception) => exception switch
    {
        BundleBuildException => "BUNDLE_BUILD_FAILED",
        InvalidOperationException => "INVALID_PUBLICATION",
        _ => "PUBLICATION_FAILED"
    };

    private static string SafeFailureMessage(Exception exception)
    {
        var message = exception.Message.ReplaceLineEndings(" ");
        return message.Length <= 500 ? message : message[..500];
    }

    [LoggerMessage(1002, LogLevel.Information, "Publishing config version {ConfigVersion} for biller {BillerId}, deployment {DeploymentId}; trace {TraceId}")]
    private static partial void LogPublicationStarted(ILogger logger, string billerId, string deploymentId, int configVersion, string? traceId);

    [LoggerMessage(1003, LogLevel.Information, "Published deployment {DeploymentId} for biller {BillerId} at {PublishedUrl}; trace {TraceId}")]
    private static partial void LogPublicationReady(ILogger logger, string billerId, string deploymentId, Uri publishedUrl, string? traceId);

    [LoggerMessage(1004, LogLevel.Information, "Publication {DeploymentId} for biller {BillerId} was cancelled; trace {TraceId}")]
    private static partial void LogPublicationCancelled(ILogger logger, string billerId, string deploymentId, string? traceId);

    [LoggerMessage(1902, LogLevel.Error, "Publication {DeploymentId} failed for biller {BillerId}; trace {TraceId}")]
    private static partial void LogPublicationFailed(ILogger logger, string billerId, string deploymentId, string? traceId, Exception exception);

    [LoggerMessage(1903, LogLevel.Error, "Could not persist failed state for publication {DeploymentId}, biller {BillerId}; trace {TraceId}")]
    private static partial void LogFailureStatusError(ILogger logger, string billerId, string deploymentId, string? traceId, Exception exception);

    [LoggerMessage(1905, LogLevel.Error, "Publication {DeploymentId} for biller {BillerId} activated but finalization failed; trace {TraceId}")]
    private static partial void LogActivatedPublicationFinalizationFailed(ILogger logger, string billerId, string deploymentId, string? traceId, Exception exception);

    [LoggerMessage(1906, LogLevel.Error, "Could not repair activated publication {DeploymentId} for biller {BillerId}; trace {TraceId}")]
    private static partial void LogActivatedPublicationRepairFailed(ILogger logger, string billerId, string deploymentId, string? traceId, Exception exception);

    [LoggerMessage(1907, LogLevel.Error, "Could not release cancelled publication claim {DeploymentId} for biller {BillerId}; trace {TraceId}")]
    private static partial void LogClaimReleaseFailed(ILogger logger, string billerId, string deploymentId, string? traceId, Exception exception);
}
