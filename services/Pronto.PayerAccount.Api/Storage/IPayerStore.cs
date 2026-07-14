using Pronto.PayerAccount.Contracts.V1.Payers;
using Pronto.ServiceDefaults.Errors;

namespace Pronto.PayerAccount.Api.Storage;

public interface IPayerStore
{
    /// <summary>Adds the payer; throws 409 already_registered on duplicate email per biller.</summary>
    Task AddAsync(PayerResponse payer, CancellationToken cancellationToken = default);

    Task<PayerResponse?> FindAsync(string billerId, string payerId, CancellationToken cancellationToken = default);
    Task<PayerResponse?> FindByAccountAsync(string billerId, string accountNumber, CancellationToken cancellationToken = default);

    Task UpdateAsync(PayerResponse payer, CancellationToken cancellationToken = default);

    /// <summary>Delete all payers in a biller's partition (nonprod test-cleanup only).</summary>
    Task PurgeByBillerAsync(string billerId, CancellationToken cancellationToken = default);
}

public sealed class InMemoryPayerStore : IPayerStore
{
    private readonly object gate = new();
    private readonly Dictionary<(string BillerId, string PayerId), PayerResponse> payers = [];

    public Task AddAsync(PayerResponse payer, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var email = Normalize(payer.Email);
            if (payers.Values.Any(existing =>
                existing.BillerId == payer.BillerId && Normalize(existing.Email) == email))
            {
                throw ServiceException.Conflict(
                    "already_registered", "email already registered for this biller");
            }

            payers[(payer.BillerId, payer.PayerId)] = payer;
        }

        return Task.CompletedTask;
    }

    public Task<PayerResponse?> FindAsync(
        string billerId, string payerId, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            return Task.FromResult(payers.GetValueOrDefault((billerId, payerId)));
        }
    }

    public Task<PayerResponse?> FindByAccountAsync(
        string billerId, string accountNumber, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            return Task.FromResult(payers.Values.FirstOrDefault(payer => payer.BillerId == billerId
                && payer.AccountNumbers.Contains(accountNumber, StringComparer.Ordinal)));
        }
    }

    public Task UpdateAsync(PayerResponse payer, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            payers[(payer.BillerId, payer.PayerId)] = payer;
        }

        return Task.CompletedTask;
    }

    public Task PurgeByBillerAsync(string billerId, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            foreach (var key in payers.Keys.Where(k => k.BillerId == billerId).ToList())
            {
                payers.Remove(key);
            }
        }

        return Task.CompletedTask;
    }

    private static string Normalize(string email) => email.Trim().ToUpperInvariant();
}
