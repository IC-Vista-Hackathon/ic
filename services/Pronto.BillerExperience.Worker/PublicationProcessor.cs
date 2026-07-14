using System.Diagnostics;
using Pronto.BillerExperience.Worker.Artifacts;
using Pronto.BillerExperience.Worker.Persistence;

namespace Pronto.BillerExperience.Worker;

public sealed partial class PublicationProcessor(
    IPublicationRepository repository,
    PublicationArtifactPlanFactory planFactory,
    IExperienceArtifactPublisher publisher,
    ILogger<PublicationProcessor> logger)
{
    public async ValueTask ProcessAsync(PublicationDeployment deployment, CancellationToken cancellationToken)
    {
        using var activity = PublicationTelemetry.Source.StartActivity("publication.process");
        activity?.SetTag("ic.biller_id", deployment.BillerId);
        activity?.SetTag("ic.deployment_id", deployment.Id);
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            LogPublicationStarted(logger, deployment.BillerId, deployment.Id, deployment.ConfigVersion, activity?.TraceId.ToString());
            var biller = await repository.GetBillerAsync(deployment.BillerId, cancellationToken);
            var experience = await repository.GetExperienceAsync(deployment.BillerId, deployment.ConfigVersion, cancellationToken);
            var plan = planFactory.Create(deployment, biller, experience);

            await publisher.PublishAsync(plan, cancellationToken);
            deployment = await repository.SaveAsync(deployment with
            {
                Status = PublicationStates.Ready,
                PublishedUrl = plan.PublishedUrl,
                UpdatedAt = DateTimeOffset.UtcNow
            }, cancellationToken);
            await repository.MarkWorkflowAsync(deployment.BillerId, deployment.ConfigVersion, published: true, cancellationToken);

            PublicationTelemetry.Publications.Add(1, new KeyValuePair<string, object?>("outcome", "ready"));
            LogPublicationReady(logger, deployment.BillerId, deployment.Id, plan.PublishedUrl, activity?.TraceId.ToString());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogPublicationCancelled(logger, deployment.BillerId, deployment.Id, activity?.TraceId.ToString());
            throw;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            PublicationTelemetry.Publications.Add(1, new KeyValuePair<string, object?>("outcome", "failed"));
            LogPublicationFailed(logger, deployment.BillerId, deployment.Id, activity?.TraceId.ToString(), exception);
            try
            {
                await repository.SaveAsync(deployment with
                {
                    Status = PublicationStates.Failed,
                    FailureCode = FailureCode(exception),
                    FailureMessage = SafeFailureMessage(exception),
                    UpdatedAt = DateTimeOffset.UtcNow
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

    private static string FailureCode(Exception exception) => exception switch
    {
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
}
