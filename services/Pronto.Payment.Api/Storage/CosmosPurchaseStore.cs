using System.Net;
using System.Text.Json.Serialization;
using Pronto.Payment.Contracts.V1.Purchases;
using Pronto.ServiceDefaults.Errors;
using Microsoft.Azure.Cosmos;

namespace Pronto.Payment.Api.Storage;

/// <summary>
/// Cosmos-backed purchase store. Container <c>purchases</c>, partition key <c>/biller_id</c>.
/// One purchase per biller is enforced by a deterministic sentinel document id.
/// </summary>
public sealed class CosmosPurchaseStore : IPurchaseStore
{
    private const string SingletonDocumentId = "00000000-0000-0000-0000-000000000001";
    private readonly Container container;

    public CosmosPurchaseStore(CosmosClient client, string databaseName)
        => container = client.GetContainer(databaseName, "purchases");

    public async Task<PurchaseReservation> ReserveAsync(
        PurchaseResponse purchase,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var partitionKey = new PartitionKey(purchase.BillerId);
        var legacy = await FindLegacyAsync(purchase.BillerId, cancellationToken);
        if (legacy is not null)
        {
            throw AlreadyPurchased(purchase.BillerId);
        }

        var document = new PurchaseDocument
        {
            Id = SingletonDocumentId,
            BillerId = purchase.BillerId,
            IdempotencyKey = idempotencyKey,
            Purchase = purchase,
        };

        try
        {
            var response = await container.CreateItemAsync(
                document,
                partitionKey,
                cancellationToken: cancellationToken);
            return new PurchaseReservation(response.Resource.Purchase, Created: true);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            var existing = await ReadSingletonAsync(purchase.BillerId, cancellationToken)
                ?? throw AlreadyPurchased(purchase.BillerId);
            if (existing.IdempotencyKey == idempotencyKey
                && existing.Purchase.Plan == purchase.Plan)
            {
                return new PurchaseReservation(existing.Purchase, Created: false);
            }

            throw AlreadyPurchased(purchase.BillerId);
        }
    }

    public async Task<PurchaseResponse> CompleteAsync(
        string billerId,
        string purchaseId,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var response = await container.ReadItemAsync<PurchaseDocument>(
                SingletonDocumentId,
                new PartitionKey(billerId),
                cancellationToken: cancellationToken);
            if (response.Resource.Purchase.PurchaseId != purchaseId)
            {
                throw ServiceException.NotFound("not_found", $"purchase {purchaseId} not found");
            }

            if (response.Resource.Purchase.Status == PurchaseStatus.Paid)
            {
                return response.Resource.Purchase;
            }

            var updated = response.Resource with
            {
                Purchase = response.Resource.Purchase with { Status = PurchaseStatus.Paid }
            };
            try
            {
                var replaced = await container.ReplaceItemAsync(
                    updated,
                    SingletonDocumentId,
                    new PartitionKey(billerId),
                    new ItemRequestOptions { IfMatchEtag = response.ETag },
                    cancellationToken);
                return replaced.Resource.Purchase;
            }
            catch (CosmosException exception) when (
                exception.StatusCode == HttpStatusCode.PreconditionFailed && attempt < 2)
            {
            }
        }

        throw ServiceException.Conflict(
            "purchase_concurrency",
            $"purchase {purchaseId} could not be completed due to concurrent updates");
    }

    public async Task<PurchaseResponse?> FindAsync(
        string billerId, string purchaseId, CancellationToken cancellationToken = default)
    {
        var singleton = await ReadSingletonAsync(billerId, cancellationToken);
        if (singleton?.Purchase.PurchaseId == purchaseId)
        {
            return singleton.Purchase;
        }

        try
        {
            var legacy = await container.ReadItemAsync<PurchaseDocument>(
                purchaseId,
                new PartitionKey(billerId),
                cancellationToken: cancellationToken);
            return legacy.Resource.Purchase;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task PurgeByBillerAsync(string billerId, CancellationToken cancellationToken = default)
    {
        var partition = new PartitionKey(billerId);
        using var iterator = container.GetItemQueryIterator<IdOnly>(
            new QueryDefinition("SELECT c.id FROM c"),
            requestOptions: new QueryRequestOptions { PartitionKey = partition });

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            foreach (var item in page)
            {
                await container.DeleteItemAsync<PurchaseDocument>(
                    item.Id, partition, cancellationToken: cancellationToken);
            }
        }
    }

    private sealed record IdOnly([property: JsonPropertyName("id")] string Id);

    private async Task<PurchaseDocument?> ReadSingletonAsync(
        string billerId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await container.ReadItemAsync<PurchaseDocument>(
                SingletonDocumentId,
                new PartitionKey(billerId),
                cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<PurchaseResponse?> FindLegacyAsync(
        string billerId,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
            "SELECT TOP 1 VALUE c.purchase FROM c WHERE c.id != @singleton")
            .WithParameter("@singleton", SingletonDocumentId);
        using var iterator = container.GetItemQueryIterator<PurchaseResponse>(
            query,
            requestOptions: new QueryRequestOptions
            {
                PartitionKey = new PartitionKey(billerId),
                MaxItemCount = 1
            });
        if (!iterator.HasMoreResults)
        {
            return null;
        }

        var page = await iterator.ReadNextAsync(cancellationToken);
        return page.FirstOrDefault();
    }

    private static ServiceException AlreadyPurchased(string billerId) =>
        ServiceException.Conflict(
            "already_purchased",
            $"biller {billerId} already purchased or started purchasing the platform");

    private sealed record PurchaseDocument
    {
        [JsonPropertyName("id")]
        public required string Id { get; init; }

        [JsonPropertyName("biller_id")]
        public required string BillerId { get; init; }

        [JsonPropertyName("idempotency_key")]
        public string? IdempotencyKey { get; init; }

        [JsonPropertyName("purchase")]
        public required PurchaseResponse Purchase { get; init; }
    }
}
