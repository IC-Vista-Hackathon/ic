using System.Diagnostics;
using System.Net;
using Microsoft.Azure.Cosmos;
using Pronto.BillerExperience.Contracts.V1.Experiences;
using Pronto.BillerExperience.Contracts.V1.Onboarding;

namespace Pronto.BillerExperience.Worker.Persistence;

public sealed partial class CosmosPublicationRepository(
    CosmosClient client,
    string databaseName,
    ILogger<CosmosPublicationRepository> logger) : IPublicationRepository
{
    private Container Billers => client.GetContainer(databaseName, "billers");
    private Container Configs => client.GetContainer(databaseName, "configs");
    private Container Deployments => client.GetContainer(databaseName, "deployments");
    private Container Runs => client.GetContainer(databaseName, "orchestration_runs");

    public async ValueTask<PublicationDeployment?> ClaimNextAsync(CancellationToken cancellationToken)
    {
        using var activity = PublicationTelemetry.Source.StartActivity("publication.claim");
        try
        {
            var query = new QueryDefinition(
                "SELECT TOP 1 * FROM c WHERE c.status = @status ORDER BY c.requested_at")
                .WithParameter("@status", PublicationStates.Requested);
            using var iterator = Deployments.GetItemQueryIterator<PublicationDeployment>(
                query,
                requestOptions: new QueryRequestOptions { MaxItemCount = 1 });
            if (!iterator.HasMoreResults)
            {
                return null;
            }

            var page = await iterator.ReadNextAsync(cancellationToken);
            var candidate = page.Resource.FirstOrDefault();
            if (candidate is null)
            {
                return null;
            }

            var claimed = candidate with
            {
                Status = PublicationStates.Applying,
                UpdatedAt = DateTimeOffset.UtcNow,
                FailureCode = null,
                FailureMessage = null
            };
            try
            {
                var response = await Deployments.ReplaceItemAsync(
                    claimed with { ETag = null },
                    claimed.Id,
                    new PartitionKey(claimed.BillerId),
                    new ItemRequestOptions { IfMatchEtag = candidate.ETag },
                    cancellationToken);
                PublicationTelemetry.Claims.Add(1, new KeyValuePair<string, object?>("outcome", "claimed"));
                LogClaimed(logger, claimed.BillerId, claimed.Id, activity?.TraceId.ToString());
                return response.Resource with { ETag = response.ETag };
            }
            catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                PublicationTelemetry.Claims.Add(1, new KeyValuePair<string, object?>("outcome", "contended"));
                LogClaimContended(logger, candidate.BillerId, candidate.Id, activity?.TraceId.ToString());
                return null;
            }
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            LogRepositoryError(logger, "claim", null, null, activity?.TraceId.ToString(), exception);
            throw;
        }
    }

    public async ValueTask<PublicationBiller> GetBillerAsync(string billerId, CancellationToken cancellationToken) =>
        await ReadRequiredAsync<PublicationBiller>(Billers, billerId, billerId, "biller", cancellationToken);

    public async ValueTask<PublicationExperience> GetExperienceAsync(string billerId, int version, CancellationToken cancellationToken) =>
        await ReadRequiredAsync<PublicationExperience>(Configs, $"config-{version}", billerId, "experience", cancellationToken);

    public async ValueTask<PublicationDeployment> SaveAsync(
        PublicationDeployment deployment,
        CancellationToken cancellationToken)
    {
        using var activity = PublicationTelemetry.Source.StartActivity("publication.status.save");
        activity?.SetTag("ic.biller_id", deployment.BillerId);
        activity?.SetTag("ic.deployment_id", deployment.Id);
        try
        {
            var response = await Deployments.ReplaceItemAsync(
                deployment with { ETag = null },
                deployment.Id,
                new PartitionKey(deployment.BillerId),
                new ItemRequestOptions { IfMatchEtag = deployment.ETag },
                cancellationToken);
            return response.Resource with { ETag = response.ETag };
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            LogRepositoryError(logger, "save", deployment.BillerId, deployment.Id, activity?.TraceId.ToString(), exception);
            throw;
        }
    }

    public async ValueTask MarkWorkflowAsync(
        string billerId,
        int version,
        bool published,
        CancellationToken cancellationToken)
    {
        using var activity = PublicationTelemetry.Source.StartActivity("publication.workflow.save");
        activity?.SetTag("ic.biller_id", billerId);
        try
        {
            var now = DateTimeOffset.UtcNow;
            await Configs.PatchItemAsync<PublicationExperience>(
                $"config-{version}",
                new PartitionKey(billerId),
                [PatchOperation.Replace("/status", published ? ExperienceRevisionState.Published : ExperienceRevisionState.Failed)],
                cancellationToken: cancellationToken);
            await Runs.PatchItemAsync<object>(
                "onboarding",
                new PartitionKey(billerId),
                [
                    PatchOperation.Replace("/state", published ? OnboardingSessionState.Published : OnboardingSessionState.Failed),
                    PatchOperation.Replace("/updated_at", now)
                ],
                cancellationToken: cancellationToken);
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            LogRepositoryError(logger, "save_workflow", billerId, $"config-{version}", activity?.TraceId.ToString(), exception);
            throw;
        }
    }

    private async ValueTask<T> ReadRequiredAsync<T>(
        Container container,
        string id,
        string partitionKey,
        string documentType,
        CancellationToken cancellationToken)
    {
        using var activity = PublicationTelemetry.Source.StartActivity($"publication.{documentType}.read");
        activity?.SetTag("ic.biller_id", partitionKey);
        try
        {
            var response = await container.ReadItemAsync<T>(id, new PartitionKey(partitionKey), cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            var missing = new InvalidOperationException($"Required {documentType} '{id}' was not found for biller '{partitionKey}'.", exception);
            activity?.SetStatus(ActivityStatusCode.Error, "not_found");
            LogRepositoryError(logger, $"read_{documentType}", partitionKey, id, activity?.TraceId.ToString(), missing);
            throw missing;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            LogRepositoryError(logger, $"read_{documentType}", partitionKey, id, activity?.TraceId.ToString(), exception);
            throw;
        }
    }

    [LoggerMessage(1100, LogLevel.Information, "Claimed publication {DeploymentId} for biller {BillerId}; trace {TraceId}")]
    private static partial void LogClaimed(ILogger logger, string billerId, string deploymentId, string? traceId);

    [LoggerMessage(1101, LogLevel.Debug, "Publication claim contention for {DeploymentId}, biller {BillerId}; trace {TraceId}")]
    private static partial void LogClaimContended(ILogger logger, string billerId, string deploymentId, string? traceId);

    [LoggerMessage(1900, LogLevel.Error, "Publication repository operation {Operation} failed for biller {BillerId}, deployment {DeploymentId}; trace {TraceId}")]
    private static partial void LogRepositoryError(ILogger logger, string operation, string? billerId, string? deploymentId, string? traceId, Exception exception);
}
