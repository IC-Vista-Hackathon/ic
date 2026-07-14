using System.Diagnostics;
using System.Net;
using IC.BillerExperience.Api.Domain;
using Microsoft.Azure.Cosmos;

namespace IC.BillerExperience.Api.Infrastructure.Persistence;

public sealed partial class CosmosBillerExperienceRepository(
    CosmosClient client,
    string databaseName,
    ILogger<CosmosBillerExperienceRepository> logger) : IBillerExperienceRepository
{
    private Container Billers => client.GetContainer(databaseName, "billers");
    private Container Configs => client.GetContainer(databaseName, "configs");
    private Container Runs => client.GetContainer(databaseName, "orchestration_runs");
    private Container Deployments => client.GetContainer(databaseName, "deployments");

    public ValueTask<BillerRecord> CreateBillerAsync(BillerRecord biller, CancellationToken cancellationToken) =>
        ObserveAsync("create", "billers", biller.Id, async () =>
        {
            var response = await Billers.CreateItemAsync(biller, new PartitionKey(biller.Id), cancellationToken: cancellationToken);
            return response.Resource;
        });

    public ValueTask<BillerRecord?> GetBillerAsync(string billerId, CancellationToken cancellationToken) =>
        ObserveAsync<BillerRecord?>("read", "billers", billerId, async () =>
        {
            try
            {
                var response = await Billers.ReadItemAsync<BillerRecord>(billerId, new PartitionKey(billerId), cancellationToken: cancellationToken);
                return response.Resource;
            }
            catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        });

    public ValueTask<BillerRecord> SaveBillerAsync(BillerRecord biller, CancellationToken cancellationToken) =>
        ObserveAsync("upsert", "billers", biller.Id, async () =>
        {
            var response = await Billers.UpsertItemAsync(biller, new PartitionKey(biller.Id), cancellationToken: cancellationToken);
            return response.Resource;
        });

    public ValueTask<ExperienceRecord?> GetLatestExperienceAsync(string billerId, CancellationToken cancellationToken) =>
        ObserveAsync<ExperienceRecord?>("query", "configs", billerId, async () =>
        {
            var query = new QueryDefinition(
                "SELECT TOP 1 * FROM c WHERE c.biller_id = @billerId ORDER BY c.version DESC")
                .WithParameter("@billerId", billerId);
            using var iterator = Configs.GetItemQueryIterator<ExperienceRecord>(query, requestOptions: new QueryRequestOptions
            {
                PartitionKey = new PartitionKey(billerId),
                MaxItemCount = 1
            });
            if (!iterator.HasMoreResults)
            {
                return null;
            }

            var page = await iterator.ReadNextAsync(cancellationToken);
            var item = page.Resource.FirstOrDefault();
            return item is null ? null : item with { ETag = page.ETag };
        });

    public ValueTask<ExperienceRecord> SaveExperienceAsync(ExperienceRecord experience, string? expectedETag, CancellationToken cancellationToken) =>
        ObserveAsync("upsert", "configs", experience.BillerId, async () =>
        {
            try
            {
                var options = expectedETag is null ? null : new ItemRequestOptions { IfMatchEtag = expectedETag };
                var response = await Configs.UpsertItemAsync(experience, new PartitionKey(experience.BillerId), options, cancellationToken);
                return response.Resource with { ETag = response.ETag };
            }
            catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                throw new ConcurrencyException("The experience was modified by another request.");
            }
        });

    public ValueTask<OnboardingRunRecord?> GetRunAsync(string billerId, string runId, CancellationToken cancellationToken) =>
        ObserveAsync<OnboardingRunRecord?>("read", "orchestration_runs", billerId, async () =>
        {
            try
            {
                var response = await Runs.ReadItemAsync<OnboardingRunRecord>(runId, new PartitionKey(billerId), cancellationToken: cancellationToken);
                return response.Resource with { ETag = response.ETag };
            }
            catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        });

    public ValueTask<OnboardingRunRecord> SaveRunAsync(OnboardingRunRecord run, string? expectedETag, CancellationToken cancellationToken) =>
        ObserveAsync("upsert", "orchestration_runs", run.BillerId, async () =>
        {
            try
            {
                var options = expectedETag is null ? null : new ItemRequestOptions { IfMatchEtag = expectedETag };
                var response = await Runs.UpsertItemAsync(run, new PartitionKey(run.BillerId), options, cancellationToken);
                return response.Resource with { ETag = response.ETag };
            }
            catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                throw new ConcurrencyException("The onboarding run was modified by another request.");
            }
        });

    public ValueTask<DeploymentRecord> CreateDeploymentAsync(DeploymentRecord deployment, CancellationToken cancellationToken) =>
        ObserveAsync("create", "deployments", deployment.BillerId, async () =>
        {
            try
            {
                var response = await Deployments.CreateItemAsync(deployment, new PartitionKey(deployment.BillerId), cancellationToken: cancellationToken);
                return response.Resource with { ETag = response.ETag };
            }
            catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
            {
                throw new ConcurrencyException("This revision already has a publication request.");
            }
        });

    public ValueTask<DeploymentRecord?> GetDeploymentAsync(string billerId, string deploymentId, CancellationToken cancellationToken) =>
        ObserveAsync<DeploymentRecord?>("read", "deployments", billerId, async () =>
        {
            try
            {
                var response = await Deployments.ReadItemAsync<DeploymentRecord>(deploymentId, new PartitionKey(billerId), cancellationToken: cancellationToken);
                return response.Resource with { ETag = response.ETag };
            }
            catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        });

    private async ValueTask<T> ObserveAsync<T>(string operation, string container, string billerId, Func<Task<T>> action)
    {
        var startedAt = Stopwatch.GetTimestamp();
        using var activity = BillerExperienceTelemetry.Source.StartActivity($"cosmos:{container}:{operation}");
        activity?.SetTag("db.system", "cosmosdb");
        activity?.SetTag("db.operation.name", operation);
        activity?.SetTag("db.namespace", container);
        activity?.SetTag("ic.biller_id", billerId);
        try
        {
            var result = await action();
            BillerExperienceTelemetry.PersistenceOperations.Add(1, new("container", container), new("operation", operation), new("outcome", "success"));
            return result;
        }
        catch (Exception exception)
        {
            LogPersistenceError(logger, operation, container, billerId, activity?.TraceId.ToString(), exception);
            BillerExperienceTelemetry.PersistenceOperations.Add(1, new("container", container), new("operation", operation), new("outcome", "error"));
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            throw;
        }
        finally
        {
            BillerExperienceTelemetry.PersistenceDuration.Record(
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                new("container", container),
                new("operation", operation));
        }
    }

    [LoggerMessage(2100, LogLevel.Error,
        "Cosmos operation {Operation} failed for container {Container}, biller {BillerId}, trace {TraceId}")]
    private static partial void LogPersistenceError(
        ILogger logger,
        string operation,
        string container,
        string billerId,
        string? traceId,
        Exception exception);
}
