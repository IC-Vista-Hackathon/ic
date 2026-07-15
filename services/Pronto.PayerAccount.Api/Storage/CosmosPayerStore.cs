using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using Pronto.PayerAccount.Contracts.V1.Payers;
using Pronto.ServiceDefaults.Errors;
using Microsoft.Azure.Cosmos;

namespace Pronto.PayerAccount.Api.Storage;

/// <summary>
/// Cosmos-backed payer store. Container <c>payer_accounts</c>, partition key <c>/biller_id</c>.
///
/// Uniqueness is enforced deterministically rather than by read-then-write: alongside each payer
/// document we write marker documents whose ids are derived from the value they guard
/// (<c>email::…</c>, <c>link::…</c>). Because Cosmos guarantees id uniqueness within a partition,
/// creating a marker that already exists fails with 409, and writing the payer + its markers in a
/// single transactional batch makes registration and account linking atomic and race-free. All
/// documents for a biller share the <c>/biller_id</c> partition, so the batch is valid.
/// </summary>
public sealed class CosmosPayerStore : IPayerStore
{
    private const int MaxConcurrencyRetries = 5;

    private readonly Container container;

    public CosmosPayerStore(CosmosClient client, string databaseName)
        => container = client.GetContainer(databaseName, "payer_accounts");

    public async Task<PayerResponse> AddAsync(PayerResponse payer, CancellationToken cancellationToken = default)
    {
        var accounts = NormalizeAccounts(payer.AccountNumbers);
        var stored = payer with { AccountNumbers = accounts };
        var partitionKey = new PartitionKey(stored.BillerId);
        if (await FindByEmailAsync(stored.BillerId, stored.Email, cancellationToken).ConfigureAwait(false)
            is not null)
        {
            throw ServiceException.Conflict(
                "already_registered",
                "email already registered for this biller");
        }

        foreach (var account in accounts)
        {
            var owner = await FindByAccountAsync(stored.BillerId, account, cancellationToken)
                .ConfigureAwait(false);
            if (owner is not null)
            {
                throw ServiceException.Conflict(
                    "account_already_linked",
                    $"account {account} is already linked to another payer for this biller");
            }
        }

        var batch = container.CreateTransactionalBatch(partitionKey);
        var kinds = new List<MarkerKind> { MarkerKind.Payer };
        batch.CreateItem(ToDocument(stored));
        batch.CreateItem(EmailMarker(stored.BillerId, stored.Email, stored.PayerId));
        kinds.Add(MarkerKind.Email(stored.Email));
        foreach (var account in accounts)
        {
            batch.CreateItem(LinkMarker(stored.BillerId, account, stored.PayerId));
            kinds.Add(MarkerKind.Link(account));
        }

        using var response = await batch.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            return stored;
        }

        ThrowForFailedBatch(response, kinds);
        throw ServiceException.Conflict("payer_write_failed", "payer registration could not be completed");
    }

    public async Task<PayerResponse?> FindAsync(
        string billerId, string payerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await container.ReadItemAsync<PayerDocument>(
                payerId, new PartitionKey(billerId), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return response.Resource.Payer;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<PayerResponse?> FindByAccountAsync(
        string billerId, string accountNumber, CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition(
                "SELECT TOP 1 VALUE c.payer FROM c WHERE ARRAY_CONTAINS(c.payer.account_numbers, @accountNumber)")
            .WithParameter("@accountNumber", accountNumber);
        using var iterator = container.GetItemQueryIterator<PayerResponse>(
            query, requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(billerId) });
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);
            var payer = page.FirstOrDefault();
            if (payer is not null) return payer;
        }

        return null;
    }

    public async Task<PayerPreferences> UpdatePreferencesAsync(
        string billerId,
        string payerId,
        Func<PayerResponse, PayerPreferences> apply,
        CancellationToken cancellationToken = default)
    {
        var partitionKey = new PartitionKey(billerId);

        for (var attempt = 0; attempt < MaxConcurrencyRetries; attempt++)
        {
            var current = await ReadDocumentAsync(billerId, payerId, cancellationToken).ConfigureAwait(false)
                ?? throw ServiceException.NotFound("not_found", $"payer {payerId} not found");

            var updated = apply(current.Document.Payer);
            var next = current.Document with { Payer = current.Document.Payer with { Preferences = updated } };

            try
            {
                await container.ReplaceItemAsync(
                    next,
                    payerId,
                    partitionKey,
                    new ItemRequestOptions { IfMatchEtag = current.ETag },
                    cancellationToken).ConfigureAwait(false);
                return updated;
            }
            catch (CosmosException exception)
                when (exception.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict)
            {
                // A concurrent write landed between the read and the replace; re-read and re-apply
                // so the interleaved change is preserved instead of being clobbered.
            }
        }

        throw ServiceException.Conflict(
            "preferences_contended", "preferences update failed after repeated concurrent modification");
    }

    public async Task<PayerResponse> LinkAccountsAsync(
        string billerId,
        string payerId,
        IReadOnlyList<string> accountNumbers,
        CancellationToken cancellationToken = default)
    {
        var partitionKey = new PartitionKey(billerId);
        var requested = NormalizeAccounts(accountNumbers);

        for (var attempt = 0; attempt < MaxConcurrencyRetries; attempt++)
        {
            var current = await ReadDocumentAsync(billerId, payerId, cancellationToken).ConfigureAwait(false)
                ?? throw ServiceException.NotFound("not_found", $"payer {payerId} not found");

            var existing = current.Document.Payer.AccountNumbers;
            var toAdd = requested
                .Where(account => !existing.Contains(account, StringComparer.Ordinal))
                .ToList();

            if (toAdd.Count == 0)
            {
                return current.Document.Payer;
            }

            foreach (var account in toAdd)
            {
                var owner = await FindByAccountAsync(billerId, account, cancellationToken)
                    .ConfigureAwait(false);
                if (owner is not null
                    && !string.Equals(owner.PayerId, payerId, StringComparison.Ordinal))
                {
                    throw ServiceException.Conflict(
                        "account_already_linked",
                        $"account {account} is already linked to another payer for this biller");
                }
            }

            var updated = current.Document.Payer with
            {
                AccountNumbers = existing.Concat(toAdd).ToList(),
            };

            var batch = container.CreateTransactionalBatch(partitionKey);
            foreach (var account in toAdd)
            {
                batch.CreateItem(LinkMarker(billerId, account, payerId));
            }

            batch.ReplaceItem(
                payerId,
                current.Document with { Payer = updated },
                new TransactionalBatchItemRequestOptions { IfMatchEtag = current.ETag });

            using var response = await batch.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return updated;
            }

            // Scan link creates: a conflict owned by another payer is terminal; a conflict we
            // already own (or a precondition failure on the payer replace) means a concurrent
            // write raced us, so re-read and retry.
            for (var i = 0; i < toAdd.Count; i++)
            {
                if (response[i].StatusCode != HttpStatusCode.Conflict)
                {
                    continue;
                }

                var owner = await ReadLinkOwnerAsync(billerId, toAdd[i], cancellationToken).ConfigureAwait(false);
                if (owner is not null && !string.Equals(owner, payerId, StringComparison.Ordinal))
                {
                    throw ServiceException.Conflict(
                        "account_already_linked",
                        $"account {toAdd[i]} is already linked to another payer for this biller");
                }
            }
        }

        throw ServiceException.Conflict(
            "account_link_contended", "account linking failed after repeated concurrent modification");
    }

    public async Task PurgeByBillerAsync(string billerId, CancellationToken cancellationToken = default)
    {
        var partition = new PartitionKey(billerId);
        using var iterator = container.GetItemQueryIterator<IdOnly>(
            new QueryDefinition("SELECT c.id FROM c"),
            requestOptions: new QueryRequestOptions { PartitionKey = partition });

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);
            foreach (var item in page)
            {
                await container.DeleteItemAsync<object>(
                    item.Id, partition, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<(PayerDocument Document, string ETag)?> ReadDocumentAsync(
        string billerId, string payerId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await container.ReadItemAsync<PayerDocument>(
                payerId, new PartitionKey(billerId), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return (response.Resource, response.ETag);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<string?> ReadLinkOwnerAsync(
        string billerId, string accountNumber, CancellationToken cancellationToken)
    {
        try
        {
            var response = await container.ReadItemAsync<MarkerDocument>(
                LinkMarkerId(accountNumber), new PartitionKey(billerId), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return response.Resource.PayerId;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<PayerResponse?> FindByEmailAsync(
        string billerId,
        string email,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                // hackathon-scan-ok: "+" joins literal SQL text across lines; the only variable input (email) is bound via WithParameter, not concatenated
                "SELECT TOP 1 VALUE c.payer FROM c "
                + "WHERE IS_DEFINED(c.payer) AND UPPER(c.payer.email) = @email")
            .WithParameter("@email", NormalizeEmail(email));
        using var iterator = container.GetItemQueryIterator<PayerResponse>(
            query,
            requestOptions: new QueryRequestOptions
            {
                PartitionKey = new PartitionKey(billerId),
                MaxItemCount = 1,
            });
        if (!iterator.HasMoreResults)
        {
            return null;
        }

        var page = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);
        return page.FirstOrDefault();
    }

    private static void ThrowForFailedBatch(TransactionalBatchResponse response, IReadOnlyList<MarkerKind> kinds)
    {
        for (var i = 0; i < response.Count && i < kinds.Count; i++)
        {
            if (response[i].StatusCode != HttpStatusCode.Conflict)
            {
                continue;
            }

            var kind = kinds[i];
            throw kind.Type switch
            {
                MarkerType.Email => ServiceException.Conflict(
                    "already_registered", "email already registered for this biller"),
                MarkerType.Link => ServiceException.Conflict(
                    "account_already_linked",
                    $"account {kind.Value} is already linked to another payer for this biller"),
                _ => ServiceException.Conflict("payer_write_failed", "payer registration conflicted"),
            };
        }
    }

    private static List<string> NormalizeAccounts(IReadOnlyList<string> accounts) => accounts
        .Select(account => account.Trim())
        .Where(account => account.Length > 0)
        .Distinct(StringComparer.Ordinal)
        .ToList();

    private static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();

    private static string EmailMarkerId(string email) => "email::" + Encode(NormalizeEmail(email));

    private static string LinkMarkerId(string accountNumber) => "link::" + Encode(accountNumber.Trim());

    // URL/id-safe deterministic encoding so account numbers or emails containing characters that
    // Cosmos disallows in an id ('/', '\\', '?', '#') can't break or collide the marker id.
    private static string Encode(string value) => Convert
        .ToBase64String(Encoding.UTF8.GetBytes(value))
        .Replace('+', '-')
        .Replace('/', '_')
        .TrimEnd('=');

    private static MarkerDocument EmailMarker(string billerId, string email, string payerId) => new()
    {
        Id = EmailMarkerId(email),
        BillerId = billerId,
        Kind = "email",
        PayerId = payerId,
        Value = NormalizeEmail(email),
    };

    private static MarkerDocument LinkMarker(string billerId, string accountNumber, string payerId) => new()
    {
        Id = LinkMarkerId(accountNumber),
        BillerId = billerId,
        Kind = "link",
        PayerId = payerId,
        Value = accountNumber.Trim(),
    };

    private static PayerDocument ToDocument(PayerResponse payer) => new()
    {
        Id = payer.PayerId,
        BillerId = payer.BillerId,
        Kind = "payer",
        Payer = payer,
    };

    private sealed record IdOnly([property: JsonPropertyName("id")] string Id);

    private enum MarkerType
    {
        Payer,
        Email,
        Link,
    }

    private readonly record struct MarkerKind(MarkerType Type, string Value)
    {
        public static readonly MarkerKind Payer = new(MarkerType.Payer, string.Empty);

        public static MarkerKind Email(string value) => new(MarkerType.Email, value);

        public static MarkerKind Link(string value) => new(MarkerType.Link, value);
    }

    private sealed record MarkerDocument
    {
        [JsonPropertyName("id")]
        public required string Id { get; init; }

        [JsonPropertyName("biller_id")]
        public required string BillerId { get; init; }

        [JsonPropertyName("kind")]
        public required string Kind { get; init; }

        [JsonPropertyName("payer_id")]
        public required string PayerId { get; init; }

        [JsonPropertyName("value")]
        public required string Value { get; init; }
    }

    private sealed record PayerDocument
    {
        [JsonPropertyName("id")]
        public required string Id { get; init; }

        [JsonPropertyName("biller_id")]
        public required string BillerId { get; init; }

        [JsonPropertyName("kind")]
        public string Kind { get; init; } = "payer";

        [JsonPropertyName("payer")]
        public required PayerResponse Payer { get; init; }
    }
}
