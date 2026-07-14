using System.Net;
using System.Text.Json.Serialization;
using IC.PayerAccount.Contracts.V1.Payers;
using IC.ServiceDefaults.Errors;
using Microsoft.Azure.Cosmos;

namespace IC.PayerAccount.Api.Storage;

/// <summary>
/// Cosmos-backed payer store. Container <c>payer_accounts</c>, partition key <c>/biller_id</c>.
/// Duplicate registration (same email per biller) is rejected with a partition-scoped,
/// case-insensitive query before insert.
/// </summary>
public sealed class CosmosPayerStore : IPayerStore
{
    private readonly Container container;

    public CosmosPayerStore(CosmosClient client, string databaseName)
        => container = client.GetContainer(databaseName, "payer_accounts");

    public async Task AddAsync(PayerResponse payer, CancellationToken cancellationToken = default)
    {
        var partitionKey = new PartitionKey(payer.BillerId);

        var query = new QueryDefinition(
                "SELECT VALUE COUNT(1) FROM c WHERE STRINGEQUALS(TRIM(c.payer.email), @email, true)")
            .WithParameter("@email", payer.Email.Trim());

        using var iterator = container.GetItemQueryIterator<int>(
            query, requestOptions: new QueryRequestOptions { PartitionKey = partitionKey });

        var existing = 0;
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            existing += page.Sum();
        }

        if (existing > 0)
        {
            throw ServiceException.Conflict(
                "already_registered", "email already registered for this biller");
        }

        await container.CreateItemAsync(ToDocument(payer), partitionKey, cancellationToken: cancellationToken);
    }

    public async Task<PayerResponse?> FindAsync(
        string billerId, string payerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await container.ReadItemAsync<PayerDocument>(
                payerId, new PartitionKey(billerId), cancellationToken: cancellationToken);
            return response.Resource.Payer;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task UpdateAsync(PayerResponse payer, CancellationToken cancellationToken = default)
        => await container.UpsertItemAsync(
            ToDocument(payer), new PartitionKey(payer.BillerId), cancellationToken: cancellationToken);

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
                await container.DeleteItemAsync<PayerDocument>(
                    item.Id, partition, cancellationToken: cancellationToken);
            }
        }
    }

    private sealed record IdOnly([property: JsonPropertyName("id")] string Id);

    private static PayerDocument ToDocument(PayerResponse payer) => new()
    {
        Id = payer.PayerId,
        BillerId = payer.BillerId,
        Payer = payer,
    };

    private sealed record PayerDocument
    {
        [JsonPropertyName("id")]
        public required string Id { get; init; }

        [JsonPropertyName("biller_id")]
        public required string BillerId { get; init; }

        [JsonPropertyName("payer")]
        public required PayerResponse Payer { get; init; }
    }
}
