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
            // Slugs must be globally unique, but Cosmos unique keys are scoped per partition and
            // billers partition on /id, so a slug reservation document in its own partition is the
            // atomic gate: the point-create below is the only thing that decides the winner of a
            // concurrent race for the same slug.
            var reservationId = SlugReservationId(biller.Slug);
            var reservationPartition = new PartitionKey(reservationId);
            try
            {
                await Billers.CreateItemAsync(
                    new SlugReservationDocument(reservationId, biller.Id, "slug_reservation"),
                    reservationPartition,
                    cancellationToken: cancellationToken);
            }
            catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
            {
                throw new SlugConflictException(biller.Slug);
            }

            try
            {
                var response = await Billers.CreateItemAsync(biller, new PartitionKey(biller.Id), cancellationToken: cancellationToken);
                return response.Resource;
            }
            catch
            {
                // Release the reservation so a retry (or a different biller) can claim the slug.
                // Use CancellationToken.None: the caller's token may already be cancelled (which is
                // what failed the create), and a skipped cleanup would orphan the reservation and
                // block the slug forever. Best-effort — the original exception is always re-thrown.
                try
                {
                    using var _ = await Billers.DeleteItemStreamAsync(reservationId, reservationPartition, cancellationToken: CancellationToken.None);
                }
                catch
                {
                    // ignored
                }

                throw;
            }
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

    public ValueTask<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken) =>
        ObserveAsync("query", "billers", slug, async () =>
        {
            // Authoritative check is a single-partition point read of the reservation document.
            var reservationId = SlugReservationId(slug);
            try
            {
                await Billers.ReadItemAsync<SlugReservationDocument>(
                    reservationId, new PartitionKey(reservationId), cancellationToken: cancellationToken);
                return true;
            }
            catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
            {
                // Fall back to legacy biller docs created before slug reservations existed.
                var query = new QueryDefinition("SELECT TOP 1 c.id FROM c WHERE c.slug = @slug")
                    .WithParameter("@slug", slug);
                using var iterator = Billers.GetItemQueryIterator<IdOnly>(
                    query, requestOptions: new QueryRequestOptions { MaxItemCount = 1 });
                if (!iterator.HasMoreResults)
                {
                    return false;
                }

                var page = await iterator.ReadNextAsync(cancellationToken);
                return page.Resource.Any();
            }
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

    public ValueTask<DeploymentRecord> SaveDeploymentAsync(
        DeploymentRecord deployment,
        string? expectedETag,
        CancellationToken cancellationToken) =>
        ObserveAsync("replace", "deployments", deployment.BillerId, async () =>
        {
            try
            {
                var options = expectedETag is null ? null : new ItemRequestOptions { IfMatchEtag = expectedETag };
                var response = await Deployments.ReplaceItemAsync(
                    deployment,
                    deployment.Id,
                    new PartitionKey(deployment.BillerId),
                    options,
                    cancellationToken);
                return response.Resource with { ETag = response.ETag };
            }
            catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                throw new ConcurrencyException("This publication request was modified by another request.");
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
        var biller = await GetBillerAsync(billerId, cancellationToken);
        await DeletePartitionAsync(Configs, partition, cancellationToken);
        await DeletePartitionAsync(Runs, partition, cancellationToken);
        await DeletePartitionAsync(Deployments, partition, cancellationToken);

        // Release the slug reservation (its own partition) so a purged slug can be reused.
        if (biller is not null)
        {
            var reservationId = SlugReservationId(biller.Slug);
            using var __ = await Billers.DeleteItemStreamAsync(
                reservationId, new PartitionKey(reservationId), cancellationToken: cancellationToken);
        }

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

    private static string SlugReservationId(string slug) => $"slug:{slug}";

    private sealed record IdOnly(string Id);

    private sealed record SlugReservationDocument(
        [property: JsonProperty("id")] string Id,
        [property: JsonProperty("biller_id")] string BillerId,
        [property: JsonProperty("document_type")] string DocumentType);

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
