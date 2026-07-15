using Pronto.Payment.Api.Purchases;
using Pronto.Payment.Api.Storage;
using Pronto.Payment.Contracts.V1.Purchases;
using Pronto.ServiceDefaults.Errors;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Pronto.Payment.Api.Tests;

public sealed class PurchaseWorkflowStoreTests
{
    private static PurchaseCompletionRunner Runner(IPurchaseCompletionOutbox outbox, FakeBillerAccountClient biller) =>
        new(outbox, biller, NullLogger<PurchaseCompletionRunner>.Instance);

    [Fact]
    public async Task CreatePendingPersistsPendingPurchaseAndDurableCompletion()
    {
        var store = new InMemoryPurchaseStore();
        var billerId = Guid.NewGuid().ToString();

        var result = await store.CreatePendingAsync(new CreatePurchaseRequest(billerId, PurchasePlan.Isolated));

        Assert.False(result.AlreadyExisted);
        Assert.Equal(PurchaseStatus.Pending, result.Purchase.Status);
        Assert.Equal(PurchasePricing.IsolatedCents, result.Purchase.AmountCents);
        var pending = await store.ListPendingCompletionsAsync(10);
        Assert.Single(pending);
        Assert.Equal(result.Purchase.PurchaseId, pending[0].PurchaseId);
    }

    [Fact]
    public void PurchaseIdIsDeterministicPerBiller()
    {
        var billerId = Guid.NewGuid().ToString();
        Assert.Equal(PurchaseIdentity.ForBiller(billerId), PurchaseIdentity.ForBiller(billerId));
        Assert.NotEqual(PurchaseIdentity.ForBiller(billerId), PurchaseIdentity.ForBiller(Guid.NewGuid().ToString()));
    }

    [Fact]
    public async Task FailedCompletionKeepsPurchasePendingAndOutboxDurable()
    {
        var store = new InMemoryPurchaseStore();
        var biller = new FakeBillerAccountClient { FailuresBeforeSuccess = int.MaxValue };
        var billerId = Guid.NewGuid().ToString();
        var created = await store.CreatePendingAsync(new CreatePurchaseRequest(billerId, PurchasePlan.Shared));

        var completion = new PurchaseCompletion(billerId, created.Purchase.PurchaseId, PurchasePlan.Shared, 0);
        var result = await Runner(store, biller).TryCompleteAsync(completion, CancellationToken.None);

        Assert.Null(result);
        var stored = await store.FindAsync(billerId, created.Purchase.PurchaseId);
        Assert.Equal(PurchaseStatus.Pending, stored!.Status);
        var pending = await store.ListPendingCompletionsAsync(10);
        Assert.Single(pending);
        Assert.Equal(1, pending[0].Attempts);
    }

    [Fact]
    public async Task DrainerCompletesPendingPurchaseAfterTransientFailure()
    {
        var store = new InMemoryPurchaseStore();
        var biller = new FakeBillerAccountClient { FailuresBeforeSuccess = 1 };
        var billerId = Guid.NewGuid().ToString();
        var created = await store.CreatePendingAsync(new CreatePurchaseRequest(billerId, PurchasePlan.Shared));
        var completion = new PurchaseCompletion(billerId, created.Purchase.PurchaseId, PurchasePlan.Shared, 0);

        Assert.Null(await Runner(store, biller).TryCompleteAsync(completion, CancellationToken.None));

        var processor = new PurchaseCompletionProcessor(
            store,
            Runner(store, biller),
            Options.Create(new PurchaseWorkflowOptions()),
            NullLogger<PurchaseCompletionProcessor>.Instance);
        var completedCount = await processor.DrainOnceAsync(50, CancellationToken.None);

        Assert.Equal(1, completedCount);
        var stored = await store.FindAsync(billerId, created.Purchase.PurchaseId);
        Assert.Equal(PurchaseStatus.Paid, stored!.Status);
        Assert.Empty(await store.ListPendingCompletionsAsync(10));
    }

    [Fact]
    public async Task ConcurrentCreatesYieldExactlyOneDurablePurchase()
    {
        var store = new InMemoryPurchaseStore();
        var billerId = Guid.NewGuid().ToString();

        var attempts = Enumerable.Range(0, 16).Select(_ => Task.Run(async () =>
        {
            try
            {
                await store.CreatePendingAsync(new CreatePurchaseRequest(billerId, PurchasePlan.Shared));
                return true;
            }
            catch (ServiceException)
            {
                return false;
            }
        }));

        var results = await Task.WhenAll(attempts);

        Assert.Single(results, created => created);
        var pending = await store.ListPendingCompletionsAsync(100);
        Assert.Single(pending);
    }

    [Fact]
    public async Task ConflictingPlanRetryIsRejected()
    {
        var store = new InMemoryPurchaseStore();
        var billerId = Guid.NewGuid().ToString();
        await store.CreatePendingAsync(new CreatePurchaseRequest(billerId, PurchasePlan.Shared, "key-1"));

        await Assert.ThrowsAsync<ServiceException>(() =>
            store.CreatePendingAsync(new CreatePurchaseRequest(billerId, PurchasePlan.Isolated, "key-1")));
    }

    [Fact]
    public async Task CompleteIsIdempotent()
    {
        var store = new InMemoryPurchaseStore();
        var biller = new FakeBillerAccountClient();
        var billerId = Guid.NewGuid().ToString();
        var created = await store.CreatePendingAsync(new CreatePurchaseRequest(billerId, PurchasePlan.Shared));
        var completion = new PurchaseCompletion(billerId, created.Purchase.PurchaseId, PurchasePlan.Shared, 0);

        var first = await Runner(store, biller).TryCompleteAsync(completion, CancellationToken.None);
        var second = await store.CompleteAsync(completion);

        Assert.Equal(PurchaseStatus.Paid, first!.Status);
        Assert.Equal(PurchaseStatus.Paid, second!.Status);
    }

    [Fact]
    public async Task RetryAfterLocalCommitFailureReusesDownstreamIdempotencyKey()
    {
        var store = new InMemoryPurchaseStore();
        var outbox = new FailOnceCompletionOutbox(store);
        var biller = new FakeBillerAccountClient();
        var billerId = Guid.NewGuid().ToString();
        var created = await store.CreatePendingAsync(
            new CreatePurchaseRequest(billerId, PurchasePlan.Shared, "create-op"));
        var completion = new PurchaseCompletion(billerId, created.Purchase.PurchaseId, PurchasePlan.Shared, 0);
        var runner = Runner(outbox, biller);

        Assert.Null(await runner.TryCompleteAsync(completion, CancellationToken.None));
        var paid = await runner.TryCompleteAsync(completion, CancellationToken.None);

        Assert.Equal(PurchaseStatus.Paid, paid!.Status);
        Assert.Equal(2, biller.Attempts.Count);
        Assert.All(biller.Attempts, attempt => Assert.Equal(created.Purchase.PurchaseId, attempt.IdempotencyKey));
    }

    [Fact]
    public async Task ConcurrentIdempotentCreatesReturnOnePurchaseIdentity()
    {
        var store = new InMemoryPurchaseStore();
        var billerId = Guid.NewGuid().ToString();
        var request = new CreatePurchaseRequest(billerId, PurchasePlan.Shared, "create-op");

        var results = await Task.WhenAll(
            Enumerable.Range(0, 16).Select(_ => Task.Run(() => store.CreatePendingAsync(request))));

        Assert.Single(results, result => !result.AlreadyExisted);
        Assert.All(results, result => Assert.Equal(results[0].Purchase.PurchaseId, result.Purchase.PurchaseId));
        Assert.Single(await store.ListPendingCompletionsAsync(100));
    }

    private sealed class FailOnceCompletionOutbox(IPurchaseCompletionOutbox inner) : IPurchaseCompletionOutbox
    {
        private int completeCalls;

        public Task<IReadOnlyList<PurchaseCompletion>> ListPendingCompletionsAsync(
            int maxCount,
            CancellationToken cancellationToken = default) =>
            inner.ListPendingCompletionsAsync(maxCount, cancellationToken);

        public Task<PurchaseResponse?> CompleteAsync(
            PurchaseCompletion completion,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref completeCalls) == 1)
            {
                return Task.FromResult<PurchaseResponse?>(null);
            }

            return inner.CompleteAsync(completion, cancellationToken);
        }

        public Task RecordCompletionFailureAsync(
            PurchaseCompletion completion,
            string failureReason,
            CancellationToken cancellationToken = default) =>
            inner.RecordCompletionFailureAsync(completion, failureReason, cancellationToken);
    }
}
