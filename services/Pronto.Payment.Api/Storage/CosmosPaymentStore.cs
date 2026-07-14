using System.Net;
using System.Text.Json.Serialization;
using Pronto.Payment.Contracts.V1.Payments;
using Microsoft.Azure.Cosmos;

namespace Pronto.Payment.Api.Storage;

/// <summary>
/// Cosmos-backed payment store. Container <c>payments</c>, partition key <c>/biller_id</c>.
/// The wire <see cref="PaymentResponse"/> is wrapped so the document carries Cosmos's
/// required <c>id</c> and the <c>biller_id</c> partition key at the top level.
/// </summary>
public sealed class CosmosPaymentStore : IPaymentStore
{
    private readonly Container container;

    public CosmosPaymentStore(CosmosClient client, string databaseName)
        => container = client.GetContainer(databaseName, "payments");

    public async Task AddAsync(PaymentResponse payment, CancellationToken cancellationToken = default)
    {
        var document = new PaymentDocument
        {
            Id = payment.PaymentId,
            BillerId = payment.BillerId,
            Payment = payment,
        };

        await container.UpsertItemAsync(
            document, new PartitionKey(payment.BillerId), cancellationToken: cancellationToken);
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
        var clauses = new List<string>();
        if (!string.IsNullOrWhiteSpace(payerAccountId)) clauses.Add("c.payment.payer_account_id = @payerAccountId");
        if (!string.IsNullOrWhiteSpace(invoiceId)) clauses.Add("c.payment.invoice_id = @invoiceId");
        var sql = "SELECT VALUE c.payment FROM c";
        if (clauses.Count > 0) sql += $" WHERE {string.Join(" AND ", clauses)}";
        sql += " ORDER BY c.payment.created_at DESC";
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

    private sealed record PaymentDocument
    {
        [JsonPropertyName("id")]
        public required string Id { get; init; }

        [JsonPropertyName("biller_id")]
        public required string BillerId { get; init; }

        [JsonPropertyName("payment")]
        public required PaymentResponse Payment { get; init; }
    }
}
