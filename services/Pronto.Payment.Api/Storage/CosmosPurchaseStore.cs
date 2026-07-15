using System.Net;
using System.Text.Json.Serialization;
using Microsoft.Azure.Cosmos;
using Pronto.Payment.Api.Purchases;
using Pronto.Payment.Contracts.V1.Purchases;

namespace Pronto.Payment.Api.Storage;

/// <summary>
/// Stores each biller's pending purchase and completion outbox in one deterministic Cosmos item.
/// Item creation atomically enforces one purchase per partition; replacement atomically marks it
/// paid and clears the completion after the downstream transition succeeds.
/// </summary>
public sealed class CosmosPurchaseStore : IPurchaseStore, IPurchaseCompletionOutbox
{
    private readonly Container container;

    public CosmosPurchaseStore(CosmosClient client, string databaseName)
        => container = client.GetContainer(databaseName, "purchases");

    public async Task<PurchaseCreateResult> CreatePendingAsync(
        CreatePurchaseRequest request,
        CancellationToken cancellationToken = default)
    {
        var purchase = new PurchaseResponse(
            PurchaseIdentity.ForBiller(request.BillerId),
            request.BillerId,
            request.Plan,
            PurchasePricing.AmountFor(request.Plan),
            PurchaseStatus.Pending);
        var document = new PurchaseDocument
        {
            Id = purchase.PurchaseId,
            BillerId = purchase.BillerId,
            Purchase = purchase,
            IdempotencyKey = NormalizeIdempotencyKey(request.IdempotencyKey),
            Completion = new PurchaseCompletion(
                purchase.BillerId,
                purchase.PurchaseId,
                purchase.Plan,
                Attempts: 0),
        };
        var partitionKey = new PartitionKey(request.BillerId);

        try
        {
            await container.CreateItemAsync(
                    document,
                    partitionKey,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return new PurchaseCreateResult(purchase, AlreadyExisted: false);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            var existing = await ReadDocumentAsync(request.BillerId, purchase.PurchaseId, cancellationToken)
                .ConfigureAwait(false);
            if (existing is null)
            {
                throw;
            }

            PurchaseRetryPolicy.EnsureIdempotentReplay(
                existing.BillerId, existing.Purchase.Plan, existing.IdempotencyKey,
                request.Plan, document.IdempotencyKey);
            return new PurchaseCreateResult(existing.Purchase, AlreadyExisted: true);
        }
    }

    public async Task<PurchaseResponse?> FindAsync(
        string billerId,
        string purchaseId,
        CancellationToken cancellationToken = default)
    {
        var document = await ReadDocumentAsync(billerId, purchaseId, cancellationToken).ConfigureAwait(false);
        return document?.Purchase;
    }

    public async Task<IReadOnlyList<PurchaseCompletion>> ListPendingCompletionsAsync(
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition(
                "SELECT TOP @maxCount VALUE c.completion FROM c WHERE IS_DEFINED(c.completion) AND NOT IS_NULL(c.completion)")
            .WithParameter("@maxCount", maxCount);
        using var iterator = container.GetItemQueryIterator<PurchaseCompletion>(query);
        var completions = new List<PurchaseCompletion>(maxCount);

        while (iterator.HasMoreResults && completions.Count < maxCount)
        {
            var page = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);
            completions.AddRange(page);
        }

        return completions;
    }

    public async Task<PurchaseResponse?> CompleteAsync(
        PurchaseCompletion completion,
        CancellationToken cancellationToken = default)
    {
        var document = await ReadDocumentAsync(
                completion.BillerId,
                completion.PurchaseId,
                cancellationToken)
            .ConfigureAwait(false);
        if (document is null)
        {
            return null;
        }

        if (document.Purchase.Status == PurchaseStatus.Paid && document.Completion is null)
        {
            return document.Purchase;
        }

        var paid = document.Purchase with { Status = PurchaseStatus.Paid };
        var completed = document with
        {
            Purchase = paid,
            Completion = null,
            LastCompletionError = null,
        };

        try
        {
            await container.ReplaceItemAsync(
                    completed,
                    completed.Id,
                    new PartitionKey(completed.BillerId),
                    new ItemRequestOptions { IfMatchEtag = document.ETag },
                    cancellationToken)
                .ConfigureAwait(false);
            return paid;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            var current = await ReadDocumentAsync(
                    completion.BillerId,
                    completion.PurchaseId,
                    cancellationToken)
                .ConfigureAwait(false);
            return current?.Purchase.Status == PurchaseStatus.Paid ? current.Purchase : null;
        }
    }

    public async Task RecordCompletionFailureAsync(
        PurchaseCompletion completion,
        string failureReason,
        CancellationToken cancellationToken = default)
    {
        var document = await ReadDocumentAsync(
                completion.BillerId,
                completion.PurchaseId,
                cancellationToken)
            .ConfigureAwait(false);
        if (document?.Completion is null)
        {
            return;
        }

        var failed = document with
        {
            Completion = document.Completion with { Attempts = document.Completion.Attempts + 1 },
            LastCompletionError = failureReason,
        };

        try
        {
            await container.ReplaceItemAsync(
                    failed,
                    failed.Id,
                    new PartitionKey(failed.BillerId),
                    new ItemRequestOptions { IfMatchEtag = document.ETag },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            // Another retry updated or completed this outbox item; preserve that newer state.
        }
    }

    public async Task PurgeByBillerAsync(string billerId, CancellationToken cancellationToken = default)
    {
        var purchaseId = PurchaseIdentity.ForBiller(billerId);
        try
        {
            await container.DeleteItemAsync<PurchaseDocument>(
                    purchaseId,
                    new PartitionKey(billerId),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
        }
    }

    private async Task<PurchaseDocument?> ReadDocumentAsync(
        string billerId,
        string purchaseId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await container.ReadItemAsync<PurchaseDocument>(
                    purchaseId,
                    new PartitionKey(billerId),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return response.Resource;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private static string? NormalizeIdempotencyKey(string? idempotencyKey) =>
        string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();

    private sealed record PurchaseDocument
    {
        [JsonPropertyName("id")]
        public required string Id { get; init; }

        [JsonPropertyName("biller_id")]
        public required string BillerId { get; init; }

        [JsonPropertyName("purchase")]
        public required PurchaseResponse Purchase { get; init; }

        [JsonPropertyName("idempotency_key")]
        public string? IdempotencyKey { get; init; }

        [JsonPropertyName("completion")]
        public PurchaseCompletion? Completion { get; init; }

        [JsonPropertyName("last_completion_error")]
        public string? LastCompletionError { get; init; }

        [JsonPropertyName("_etag")]
        public string? ETag { get; init; }
    }
}
