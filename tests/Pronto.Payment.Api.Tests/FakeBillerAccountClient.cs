using Pronto.Payment.Api.Clients;
using Pronto.Payment.Contracts.V1.Purchases;

namespace Pronto.Payment.Api.Tests;

/// <summary>
/// Controllable <see cref="IBillerAccountClient"/> for exercising the recoverable purchase
/// workflow: fail the first <see cref="FailuresBeforeSuccess"/> calls, then succeed. Records every
/// call so tests can assert idempotent retry behaviour.
/// </summary>
public sealed class FakeBillerAccountClient : IBillerAccountClient
{
    private readonly object gate = new();
    private int calls;

    public int FailuresBeforeSuccess { get; set; }

    public int Calls
    {
        get { lock (gate) { return calls; } }
    }

    public List<(string BillerId, PurchasePlan Plan, string IdempotencyKey)> Attempts { get; } = [];

    public List<(string BillerId, PurchasePlan Plan, string IdempotencyKey)> Advances { get; } = [];

    public Task AdvanceToPurchasedAsync(
        string billerId, PurchasePlan plan, string idempotencyKey, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            calls++;
            Attempts.Add((billerId, plan, idempotencyKey));
            if (calls <= FailuresBeforeSuccess)
            {
                throw new InvalidOperationException("biller account service unavailable");
            }

            Advances.Add((billerId, plan, idempotencyKey));
        }

        return Task.CompletedTask;
    }
}
