using System.Net;
using Pronto.Invoice.Api.Domain;
using Microsoft.Azure.Cosmos;

namespace Pronto.Invoice.Api.Repositories;

/// <summary>
/// Cosmos-backed invoice store. Container <c>invoices</c>, partition key <c>/biller_id</c>
/// (design/entities.md). Documents serialize snake_case via the shared STJ serializer, so
/// <see cref="InvoiceDocument.Id"/> maps to Cosmos's required <c>id</c> field and
/// <see cref="InvoiceDocument.BillerId"/> to the <c>biller_id</c> partition key.
/// </summary>
public sealed class CosmosInvoiceRepository : IInvoiceRepository
{
    private const int MaxTransitionRetries = 5;

    private readonly Container container;

    public CosmosInvoiceRepository(CosmosClient client, string databaseName)
        => container = client.GetContainer(databaseName, "invoices");

    public async Task AddRangeAsync(
        IEnumerable<InvoiceDocument> invoices, CancellationToken cancellationToken = default)
    {
        foreach (var invoice in invoices)
        {
            await container.UpsertItemAsync(
                invoice, new PartitionKey(invoice.BillerId), cancellationToken: cancellationToken);
        }
    }

    public async Task<IReadOnlyList<InvoiceDocument>> GetOpenAsync(
        string billerId, string accountNumber, CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.account_number = @accountNumber AND c.status != @paid")
            .WithParameter("@accountNumber", accountNumber)
            .WithParameter("@paid", "paid");

        using var iterator = container.GetItemQueryIterator<InvoiceDocument>(
            query, requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(billerId) });

        var results = new List<InvoiceDocument>();
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(page);
        }

        return results;
    }

    public async Task<IReadOnlyList<InvoiceDocument>> GetByAccountAsync(
        string billerId, string accountNumber, CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.account_number = @accountNumber ORDER BY c.due_date DESC")
            .WithParameter("@accountNumber", accountNumber);
        using var iterator = container.GetItemQueryIterator<InvoiceDocument>(
            query, requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(billerId) });
        var results = new List<InvoiceDocument>();
        while (iterator.HasMoreResults)
        {
            results.AddRange(await iterator.ReadNextAsync(cancellationToken));
        }

        return results;
    }

    public async Task<InvoiceDocument?> FindAsync(
        string billerId, string invoiceId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await container.ReadItemAsync<InvoiceDocument>(
                invoiceId, new PartitionKey(billerId), cancellationToken: cancellationToken);
            return response.Resource;
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
                await container.DeleteItemAsync<InvoiceDocument>(
                    item.Id, partition, cancellationToken: cancellationToken);
            }
        }
    }

    private sealed record IdOnly(string Id);

    public async Task<InvoiceTransitionResult> TryUpdateStatusAsync(
        string billerId,
        string invoiceId,
        InvoiceStatus target,
        string paymentId,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            ItemResponse<InvoiceDocument> current;
            try
            {
                current = await container.ReadItemAsync<InvoiceDocument>(
                    invoiceId, new PartitionKey(billerId), cancellationToken: cancellationToken);
            }
            catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
            {
                return new InvoiceTransitionResult(InvoiceTransitionOutcome.NotFound, null);
            }

            var invoice = current.Resource;

            var decision = InvoiceTransitionRules.Decide(
                invoice.Status, invoice.LastPaymentId, target, paymentId);
            if (decision != TransitionDecision.Apply)
            {
                return new InvoiceTransitionResult(decision.ToOutcome(), invoice);
            }

            var updated = new InvoiceDocument
            {
                Id = invoice.Id,
                BillerId = invoice.BillerId,
                AccountNumber = invoice.AccountNumber,
                PayerName = invoice.PayerName,
                Description = invoice.Description,
                AmountCents = invoice.AmountCents,
                DueDate = invoice.DueDate,
                Status = target,
                LastPaymentId = paymentId,
            };

            try
            {
                var response = await container.ReplaceItemAsync(
                    updated,
                    invoiceId,
                    new PartitionKey(billerId),
                    new ItemRequestOptions { IfMatchEtag = current.ETag },
                    cancellationToken);
                return new InvoiceTransitionResult(InvoiceTransitionOutcome.Updated, response.Resource);
            }
            catch (CosmosException exception)
                when (exception.StatusCode == HttpStatusCode.PreconditionFailed
                    && attempt < MaxTransitionRetries)
            {
                // Lost the optimistic race; re-read and re-evaluate.
            }
        }
    }
}
