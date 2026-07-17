using System.Globalization;
using System.Net;
using System.Text.Json.Serialization;
using Pronto.Payment.Api.Domain;
using Microsoft.Azure.Cosmos;

namespace Pronto.Payment.Api.Storage;

/// <summary>
/// Cosmos-backed payment store. Container <c>payments</c>, partition key <c>/biller_id</c>.
/// The <see cref="PaymentRecord"/> is wrapped so the document carries Cosmos's required
/// <c>id</c> and the <c>biller_id</c> partition key at the top level while the queryable payment
/// fields (lifecycle, scheduled_for, lease) live under <c>payment</c>.
/// </summary>
public sealed class CosmosPaymentStore : IPaymentStore
{
    private const int MaxClaimRetries = 5;

    private readonly Container container;

    public CosmosPaymentStore(CosmosClient client, string databaseName)
        => container = client.GetContainer(databaseName, "payments");

    public async Task<PaymentBeginResult> BeginAsync(
        PaymentRecord pending, CancellationToken cancellationToken = default)
    {
        var document = PaymentDocument.From(pending);
        try
        {
            var created = await container.CreateItemAsync(
                document, new PartitionKey(pending.BillerId), cancellationToken: cancellationToken);
            return new PaymentBeginResult(
                Created: true,
                created.Resource.Payment with { ETag = created.ETag });
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            // Idempotent insert lost the race (or is a client retry): return the existing record.
            var existing = await FindAsync(pending.BillerId, pending.PaymentId, cancellationToken)
                .ConfigureAwait(false);
            if (existing is null)
            {
                throw;
            }

            return new PaymentBeginResult(Created: false, existing);
        }
    }

    public async Task<PaymentRecord> SaveAsync(PaymentRecord record, CancellationToken cancellationToken = default)
    {
        var desired = record;
        for (var attempt = 0; attempt < MaxClaimRetries; attempt++)
        {
            var current = desired.ETag is null
                ? await ReadDocumentAsync(desired.BillerId, desired.PaymentId, cancellationToken)
                    .ConfigureAwait(false)
                : null;
            var etag = desired.ETag ?? current?.ETag;
            if (etag is null)
            {
                throw new InvalidOperationException(
                    $"payment {desired.PaymentId} must exist before its lifecycle can be updated");
            }

            try
            {
                var response = await container.ReplaceItemAsync(
                    PaymentDocument.From(desired),
                    desired.PaymentId,
                    new PartitionKey(desired.BillerId),
                    new ItemRequestOptions { IfMatchEtag = etag },
                    cancellationToken);
                return response.Resource.Payment with { ETag = response.ETag };
            }
            catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                var latest = await FindAsync(desired.BillerId, desired.PaymentId, cancellationToken)
                    .ConfigureAwait(false);
                if (latest is null)
                {
                    throw;
                }

                if (!ShouldAdvance(latest.Lifecycle, desired.Lifecycle))
                {
                    return latest;
                }

                desired = desired with { ETag = latest.ETag };
            }
        }

        throw new InvalidOperationException(
            $"payment {record.PaymentId} could not be updated after repeated concurrent modification");
    }

    public async Task<PaymentRecord?> FindAsync(
        string billerId, string paymentId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await container.ReadItemAsync<PaymentDocument>(
                paymentId, new PartitionKey(billerId), cancellationToken: cancellationToken);
            return response.Resource.Payment with { ETag = response.ETag };
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<PaymentRecord>> ListAsync(
        string billerId,
        string? payerAccountId,
        string? invoiceId,
        CancellationToken cancellationToken = default)
    {
        var clauses = new List<string> { "IS_DEFINED(c.payment) AND c.payment.lifecycle != @pending" };
        if (!string.IsNullOrWhiteSpace(payerAccountId)) clauses.Add("c.payment.payer_account_id = @payerAccountId");
        if (!string.IsNullOrWhiteSpace(invoiceId)) clauses.Add("c.payment.invoice_id = @invoiceId");
        var sql = $"SELECT VALUE c.payment FROM c WHERE {string.Join(" AND ", clauses)} ORDER BY c.payment.created_at DESC";
        var query = new QueryDefinition(sql).WithParameter("@pending", "pending");
        if (!string.IsNullOrWhiteSpace(payerAccountId)) query.WithParameter("@payerAccountId", payerAccountId);
        if (!string.IsNullOrWhiteSpace(invoiceId)) query.WithParameter("@invoiceId", invoiceId);
        using var iterator = container.GetItemQueryIterator<PaymentRecord>(
            query, requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(billerId) });
        var results = new List<PaymentRecord>();
        while (iterator.HasMoreResults)
        {
            results.AddRange(await iterator.ReadNextAsync(cancellationToken));
        }

        return results;
    }

    public async Task<PaymentRecord?> ClaimDueAsync(
        DateOnly asOf,
        DateTimeOffset now,
        DateTimeOffset staleBefore,
        DateTimeOffset leaseUntil,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < MaxClaimRetries; attempt++)
        {
            var query = new QueryDefinition(
                "SELECT TOP 1 * FROM c WHERE IS_DEFINED(c.payment) AND "
                + "(NOT IS_DEFINED(c.payment.lease_until) OR c.payment.lease_until = null OR c.payment.lease_until <= @now) AND ("
                + "(c.payment.lifecycle = @scheduled AND IS_DEFINED(c.payment.scheduled_for) AND c.payment.scheduled_for <= @asOf) "
                + "OR (c.payment.lifecycle = @pending AND c.payment.updated_at <= @staleBefore)) "
                + "ORDER BY c.payment.updated_at")
                .WithParameter("@now", now)
                .WithParameter("@asOf", asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                .WithParameter("@staleBefore", staleBefore)
                .WithParameter("@scheduled", "scheduled")
                .WithParameter("@pending", "pending");

            using var iterator = container.GetItemQueryIterator<PaymentDocument>(
                query, requestOptions: new QueryRequestOptions { MaxItemCount = 1 });
            if (!iterator.HasMoreResults)
            {
                return null;
            }

            var page = await iterator.ReadNextAsync(cancellationToken);
            var candidate = page.FirstOrDefault();
            if (candidate is null)
            {
                return null;
            }

            var claimed = candidate with
            {
                Payment = candidate.Payment with { LeaseUntil = leaseUntil, UpdatedAt = now },
            };

            try
            {
                var response = await container.ReplaceItemAsync(
                    claimed,
                    claimed.Id,
                    new PartitionKey(claimed.BillerId),
                    new ItemRequestOptions { IfMatchEtag = candidate.ETag },
                    cancellationToken);
                return response.Resource.Payment with { ETag = response.ETag };
            }
            catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                // Another processor claimed it first; re-query for the next candidate.
            }
        }

        return null;
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

    public async IAsyncEnumerable<PaymentRecord> EnumerateAsync(
        string? billerId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var requestOptions = string.IsNullOrWhiteSpace(billerId)
            ? new QueryRequestOptions()
            : new QueryRequestOptions { PartitionKey = new PartitionKey(billerId) };

        using var iterator = container.GetItemQueryIterator<PaymentDocument>(
            new QueryDefinition("SELECT * FROM c WHERE IS_DEFINED(c.payment)"),
            requestOptions: requestOptions);

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);
            foreach (var document in page)
            {
                yield return document.Payment with { ETag = document.ETag };
            }
        }
    }

    private sealed record IdOnly([property: JsonPropertyName("id")] string Id);

    private static bool ShouldAdvance(PaymentLifecycle current, PaymentLifecycle target) =>
        current == PaymentLifecycle.Pending && target != PaymentLifecycle.Pending
        || current == PaymentLifecycle.Scheduled && target == PaymentLifecycle.Succeeded;

    private async Task<PaymentDocument?> ReadDocumentAsync(
        string billerId,
        string paymentId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await container.ReadItemAsync<PaymentDocument>(
                paymentId,
                new PartitionKey(billerId),
                cancellationToken: cancellationToken);
            return response.Resource with { ETag = response.ETag };
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private sealed record PaymentDocument
    {
        [JsonPropertyName("id")]
        public required string Id { get; init; }

        [JsonPropertyName("biller_id")]
        public required string BillerId { get; init; }

        [JsonPropertyName("payment")]
        public required PaymentRecord Payment { get; init; }

        [JsonPropertyName("_etag")]
        public string? ETag { get; init; }

        public static PaymentDocument From(PaymentRecord record) => new()
        {
            Id = record.PaymentId,
            BillerId = record.BillerId,
            Payment = record,
        };
    }
}
