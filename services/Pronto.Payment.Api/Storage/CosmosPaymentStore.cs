using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Pronto.Payment.Contracts.V1.Payments;
using Microsoft.Azure.Cosmos;

namespace Pronto.Payment.Api.Storage;

/// <summary>
/// Cosmos-backed payment store. Container <c>payments</c>, partition key <c>/biller_id</c>.
/// The wire <see cref="PaymentResponse"/> is wrapped so the document carries Cosmos's
/// required <c>id</c> and the <c>biller_id</c> partition key at the top level. Idempotency keys
/// are reserved as sibling marker documents (no <c>payment</c> field) in the same partition, so
/// point reads / conditional creates enforce single-use without a second container; payment
/// queries filter with <c>IS_DEFINED(c.payment)</c> to skip those markers.
/// </summary>
public sealed class CosmosPaymentStore : IPaymentStore
{
    private readonly Container container;

    public CosmosPaymentStore(CosmosClient client, string databaseName)
        => container = client.GetContainer(databaseName, "payments");

    public async Task<PaymentCreation> CreatePendingAsync(
        PaymentResponse payment, string? idempotencyKey, CancellationToken cancellationToken = default)
    {
        var partitionKey = new PartitionKey(payment.BillerId);

        if (idempotencyKey is not null)
        {
            var replay = await FindByIdempotencyKeyAsync(payment.BillerId, idempotencyKey, cancellationToken);
            if (replay is not null)
            {
                return new PaymentCreation(replay, IsReplay: true);
            }
        }

        await container.CreateItemAsync(
            new PaymentDocument { Id = payment.PaymentId, BillerId = payment.BillerId, Payment = payment },
            partitionKey,
            cancellationToken: cancellationToken);

        if (idempotencyKey is not null)
        {
            try
            {
                await container.CreateItemAsync(
                    new IdempotencyDocument
                    {
                        Id = MarkerId(idempotencyKey),
                        BillerId = payment.BillerId,
                        IdempotencyKey = idempotencyKey,
                        PaymentId = payment.PaymentId,
                    },
                    partitionKey,
                    cancellationToken: cancellationToken);
            }
            catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
            {
                // Lost the race to reserve the key; the winner's payment (written before its marker)
                // is authoritative. Our own pending row is left as a recoverable, auditable orphan.
                var replay = await FindByIdempotencyKeyAsync(payment.BillerId, idempotencyKey, cancellationToken);
                if (replay is not null)
                {
                    return new PaymentCreation(replay, IsReplay: true);
                }
            }
        }

        return new PaymentCreation(payment, IsReplay: false);
    }

    public async Task UpdateAsync(PaymentResponse payment, CancellationToken cancellationToken = default)
    {
        await container.UpsertItemAsync(
            new PaymentDocument { Id = payment.PaymentId, BillerId = payment.BillerId, Payment = payment },
            new PartitionKey(payment.BillerId),
            cancellationToken: cancellationToken);
    }

    public async Task<PaymentResponse?> FindAsync(
        string billerId, string paymentId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await container.ReadItemAsync<PaymentDocument>(
                paymentId, new PartitionKey(billerId), cancellationToken: cancellationToken);
            return response.Resource.Payment;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<PaymentResponse>> ListAsync(
        string billerId,
        string? payerAccountId,
        string? invoiceId,
        CancellationToken cancellationToken = default)
    {
        var clauses = new List<string> { "IS_DEFINED(c.payment)" };
        if (!string.IsNullOrWhiteSpace(payerAccountId)) clauses.Add("c.payment.payer_account_id = @payerAccountId");
        if (!string.IsNullOrWhiteSpace(invoiceId)) clauses.Add("c.payment.invoice_id = @invoiceId");
        var sql = $"SELECT VALUE c.payment FROM c WHERE {string.Join(" AND ", clauses)} ORDER BY c.payment.created_at DESC";
        var query = new QueryDefinition(sql);
        if (!string.IsNullOrWhiteSpace(payerAccountId)) query.WithParameter("@payerAccountId", payerAccountId);
        if (!string.IsNullOrWhiteSpace(invoiceId)) query.WithParameter("@invoiceId", invoiceId);
        using var iterator = container.GetItemQueryIterator<PaymentResponse>(
            query, requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(billerId) });
        var results = new List<PaymentResponse>();
        while (iterator.HasMoreResults)
        {
            results.AddRange(await iterator.ReadNextAsync(cancellationToken));
        }

        return results;
    }

    public async Task<IReadOnlyList<PaymentResponse>> ListDueScheduledAsync(
        DateOnly asOf, CancellationToken cancellationToken = default)
    {
        // Cross-partition: the executor discovers due payments across billers, then does its
        // follow-up (invoice transition, status mark) partition-scoped by each payment's biller_id.
        var query = new QueryDefinition(
                "SELECT VALUE c.payment FROM c WHERE IS_DEFINED(c.payment) "
                + "AND c.payment.status = 'scheduled' AND c.payment.scheduled_for <= @asOf "
                + "ORDER BY c.payment.scheduled_for")
            .WithParameter("@asOf", asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        using var iterator = container.GetItemQueryIterator<PaymentResponse>(query);
        var results = new List<PaymentResponse>();
        while (iterator.HasMoreResults)
        {
            results.AddRange(await iterator.ReadNextAsync(cancellationToken));
        }

        return results;
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
                await container.DeleteItemAsync<PaymentDocument>(
                    item.Id, partition, cancellationToken: cancellationToken);
            }
        }
    }

    public async Task<PaymentResponse?> FindByIdempotencyKeyAsync(
        string billerId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var marker = await container.ReadItemAsync<IdempotencyDocument>(
                MarkerId(idempotencyKey), new PartitionKey(billerId), cancellationToken: cancellationToken);
            return await FindAsync(billerId, marker.Resource.PaymentId, cancellationToken);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    // Cosmos ids may not contain '/', '\\', '?' or '#', and the raw key is caller-supplied, so
    // derive a stable id-safe marker id from a hash of the key.
    private static string MarkerId(string idempotencyKey)
        => "idem:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey)));

    private sealed record IdOnly([property: JsonPropertyName("id")] string Id);

    private sealed record PaymentDocument
    {
        [JsonPropertyName("id")]
        public required string Id { get; init; }

        [JsonPropertyName("biller_id")]
        public required string BillerId { get; init; }

        [JsonPropertyName("payment")]
        public required PaymentResponse Payment { get; init; }
    }

    private sealed record IdempotencyDocument
    {
        [JsonPropertyName("id")]
        public required string Id { get; init; }

        [JsonPropertyName("biller_id")]
        public required string BillerId { get; init; }

        [JsonPropertyName("idempotency_key")]
        public required string IdempotencyKey { get; init; }

        [JsonPropertyName("payment_id")]
        public required string PaymentId { get; init; }
    }
}
