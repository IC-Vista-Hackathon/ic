using System.Diagnostics;
using System.Net;
using Pronto.BillerExperience.Api.Domain;
using Pronto.BillerExperience.Contracts.V1.Onboarding;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;

namespace Pronto.BillerExperience.Api.Infrastructure.Persistence;

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

    public async ValueTask AppendAgentActivityAsync(
        string billerId,
        string runId,
        AgentActivityEvent activity,
        CancellationToken cancellationToken)
    {
        await ObserveAsync("append", "orchestration_runs", billerId, async () =>
        {
            var document = new AgentActivityDocument(
                $"activity-{runId}-{activity.EventId}", billerId, runId, "agent_activity", activity);
            await Runs.CreateItemAsync(document, new PartitionKey(billerId), cancellationToken: cancellationToken);
            return true;
        });
    }

    public ValueTask<IReadOnlyList<AgentActivityEvent>> GetAgentActivityAsync(
        string billerId,
        string runId,
        CancellationToken cancellationToken) =>
        ObserveAsync<IReadOnlyList<AgentActivityEvent>>("query_activity", "orchestration_runs", billerId, async () =>
        {
            var query = new QueryDefinition(
                    "SELECT c.event FROM c WHERE c.document_type = @type AND c.run_id = @runId")
                .WithParameter("@type", "agent_activity")
                .WithParameter("@runId", runId);
            using var iterator = Runs.GetItemQueryIterator<AgentActivityProjection>(query, requestOptions: new QueryRequestOptions
            {
                PartitionKey = new PartitionKey(billerId),
                MaxItemCount = 100
            });
            var events = new List<AgentActivityEvent>();
            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(cancellationToken);
                events.AddRange(page.Resource.Select(item => item.Event));
            }
            return events.OrderBy(item => item.Sequence).ThenBy(item => item.OccurredAt).TakeLast(100).ToArray();
        });

    public ValueTask<AgentContextRecord?> GetAgentContextAsync(
        string billerId,
        string runId,
        CancellationToken cancellationToken) =>
        ObserveAsync<AgentContextRecord?>("read_context", "orchestration_runs", billerId, async () =>
        {
            try
            {
                var response = await Runs.ReadItemAsync<AgentContextRecord>(
                    $"context-{runId}",
                    new PartitionKey(billerId),
                    cancellationToken: cancellationToken);
                return response.Resource with { ETag = response.ETag };
            }
            catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        });

    public ValueTask<AgentContextRecord> SaveAgentContextAsync(
        AgentContextRecord context,
        string? expectedETag,
        CancellationToken cancellationToken) =>
        ObserveAsync("upsert_context", "orchestration_runs", context.BillerId, async () =>
        {
            try
            {
                var options = expectedETag is null ? null : new ItemRequestOptions { IfMatchEtag = expectedETag };
                var response = await Runs.UpsertItemAsync(
                    context,
                    new PartitionKey(context.BillerId),
                    options,
                    cancellationToken);
                return response.Resource with { ETag = response.ETag };
            }
            catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                throw new ConcurrencyException("The shared agent context was modified by another request.");
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

    public async ValueTask PurgeByBillerAsync(string billerId, CancellationToken cancellationToken)
    {
        var partition = new PartitionKey(billerId);
        await DeletePartitionAsync(Configs, partition, cancellationToken);
        await DeletePartitionAsync(Runs, partition, cancellationToken);
        await DeletePartitionAsync(Deployments, partition, cancellationToken);

        // The billers container is partitioned by /id (which equals the biller id).
        // DeleteItemStreamAsync returns a ResponseMessage and does NOT throw on a non-success
        // status, so a missing biller (404) is naturally a no-op — no try/catch required.
        using var _ = await Billers.DeleteItemStreamAsync(billerId, partition, cancellationToken: cancellationToken);
    }

    private static async Task DeletePartitionAsync(
        Container container, PartitionKey partition, CancellationToken cancellationToken)
    {
        using var iterator = container.GetItemQueryIterator<IdOnly>(
            new QueryDefinition("SELECT c.id FROM c"),
            requestOptions: new QueryRequestOptions { PartitionKey = partition });

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            foreach (var item in page)
            {
                using var _ = await container.DeleteItemStreamAsync(item.Id, partition, cancellationToken: cancellationToken);
            }
        }
    }

    private sealed record IdOnly(string Id);

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

    private sealed record AgentActivityDocument(
        [property: JsonProperty("id")] string Id,
        [property: JsonProperty("biller_id")] string BillerId,
        [property: JsonProperty("run_id")] string RunId,
        [property: JsonProperty("document_type")] string DocumentType,
        [property: JsonProperty("event")] AgentActivityEvent Event);

    private sealed record AgentActivityProjection(
        [property: JsonProperty("event")] AgentActivityEvent Event);
}
