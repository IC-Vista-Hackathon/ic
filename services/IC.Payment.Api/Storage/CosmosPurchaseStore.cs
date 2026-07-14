using System.Net;
using System.Text.Json.Serialization;
using IC.Payment.Contracts.V1.Purchases;
using IC.ServiceDefaults.Errors;
using Microsoft.Azure.Cosmos;

namespace IC.Payment.Api.Storage;

/// <summary>
/// Cosmos-backed purchase store. Container <c>purchases</c>, partition key <c>/biller_id</c>.
/// One purchase per biller — enforced with a partition-scoped count before insert (best-effort
/// at hackathon scale; Cosmos has no cross-document uniqueness constraint).
/// </summary>
public sealed class CosmosPurchaseStore : IPurchaseStore
{
    private readonly Container container;

    public CosmosPurchaseStore(CosmosClient client, string databaseName)
        => container = client.GetContainer(databaseName, "purchases");

    public async Task AddAsync(PurchaseResponse purchase, CancellationToken cancellationToken = default)
    {
        var partitionKey = new PartitionKey(purchase.BillerId);

        using var iterator = container.GetItemQueryIterator<int>(
            new QueryDefinition("SELECT VALUE COUNT(1) FROM c"),
            requestOptions: new QueryRequestOptions { PartitionKey = partitionKey });

        var existing = 0;
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            existing += page.Sum();
        }

        if (existing > 0)
        {
            throw ServiceException.Conflict(
                "already_purchased", $"biller {purchase.BillerId} already purchased the platform");
        }

        var document = new PurchaseDocument
        {
            Id = purchase.PurchaseId,
            BillerId = purchase.BillerId,
            Purchase = purchase,
        };

        await container.CreateItemAsync(document, partitionKey, cancellationToken: cancellationToken);
    }

    public async Task<PurchaseResponse?> FindAsync(
        string billerId, string purchaseId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await container.ReadItemAsync<PurchaseDocument>(
                purchaseId, new PartitionKey(billerId), cancellationToken: cancellationToken);
            return response.Resource.Purchase;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private sealed record PurchaseDocument
    {
        [JsonPropertyName("id")]
        public required string Id { get; init; }

        [JsonPropertyName("biller_id")]
        public required string BillerId { get; init; }

        [JsonPropertyName("purchase")]
        public required PurchaseResponse Purchase { get; init; }
    }
}
