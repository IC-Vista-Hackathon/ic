using Pronto.PayerAccount.Contracts.V1.Payers;
using Pronto.ServiceDefaults.Errors;

namespace Pronto.PayerAccount.Api.Storage;

public interface IPayerStore
{
    /// <summary>
    /// Adds the payer, atomically enforcing per-biller uniqueness of the email and of every
    /// linked account number. Throws 409 <c>already_registered</c> on a duplicate email and 409
    /// <c>account_already_linked</c> if any account is already linked to another payer.
    /// </summary>
    Task<PayerResponse> AddAsync(PayerResponse payer, CancellationToken cancellationToken = default);

    Task<PayerResponse?> FindAsync(string billerId, string payerId, CancellationToken cancellationToken = default);
    Task<PayerResponse?> FindByAccountAsync(string billerId, string accountNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies <paramref name="apply"/> to the payer and persists the resulting preferences under
    /// optimistic concurrency, retrying on a concurrent write so no interleaved PATCH is lost.
    /// <paramref name="apply"/> receives the latest stored payer on every attempt (so channel
    /// validation sees the current phone/email) and may throw <see cref="ServiceException"/> to
    /// reject the merged result. Throws 404 <c>not_found</c> if the payer does not exist.
    /// </summary>
    Task<PayerPreferences> UpdatePreferencesAsync(
        string billerId,
        string payerId,
        Func<PayerResponse, PayerPreferences> apply,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotently links account numbers to an existing payer, atomically enforcing per-biller
    /// link uniqueness. Accounts the payer already holds are ignored; an account already linked to
    /// a different payer throws 409 <c>account_already_linked</c>. Throws 404 <c>not_found</c> if
    /// the payer does not exist.
    /// </summary>
    Task<PayerResponse> LinkAccountsAsync(
        string billerId,
        string payerId,
        IReadOnlyList<string> accountNumbers,
        CancellationToken cancellationToken = default);

    /// <summary>Delete all payers in a biller's partition (nonprod test-cleanup only).</summary>
    Task PurgeByBillerAsync(string billerId, CancellationToken cancellationToken = default);
}

public sealed class InMemoryPayerStore : IPayerStore
{
    private readonly object gate = new();
    private readonly Dictionary<(string BillerId, string PayerId), PayerResponse> payers = [];

    public Task<PayerResponse> AddAsync(PayerResponse payer, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var email = NormalizeEmail(payer.Email);
            if (payers.Values.Any(existing =>
                existing.BillerId == payer.BillerId && NormalizeEmail(existing.Email) == email))
            {
                throw ServiceException.Conflict(
                    "already_registered", "email already registered for this biller");
            }

            var accounts = NormalizeAccounts(payer.AccountNumbers);
            foreach (var account in accounts)
            {
                if (IsAccountLinked(payer.BillerId, account, payerId: null))
                {
                    throw ServiceException.Conflict(
                        "account_already_linked",
                        $"account {account} is already linked to another payer for this biller");
                }
            }

            var stored = payer with { AccountNumbers = accounts };
            payers[(stored.BillerId, stored.PayerId)] = stored;
            return Task.FromResult(stored);
        }
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

    public Task<PayerPreferences> UpdatePreferencesAsync(
        string billerId,
        string payerId,
        Func<PayerResponse, PayerPreferences> apply,
        CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var payer = payers.GetValueOrDefault((billerId, payerId))
                ?? throw ServiceException.NotFound("not_found", $"payer {payerId} not found");

            var updated = apply(payer);
            payers[(billerId, payerId)] = payer with { Preferences = updated };
            return Task.FromResult(updated);
        }
    }

    public Task<PayerResponse> LinkAccountsAsync(
        string billerId,
        string payerId,
        IReadOnlyList<string> accountNumbers,
        CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var payer = payers.GetValueOrDefault((billerId, payerId))
                ?? throw ServiceException.NotFound("not_found", $"payer {payerId} not found");

            var linked = payer.AccountNumbers.ToList();
            foreach (var account in NormalizeAccounts(accountNumbers))
            {
                if (linked.Contains(account, StringComparer.Ordinal))
                {
                    continue;
                }

                if (IsAccountLinked(billerId, account, payerId))
                {
                    throw ServiceException.Conflict(
                        "account_already_linked",
                        $"account {account} is already linked to another payer for this biller");
                }

                linked.Add(account);
            }

            var updated = payer with { AccountNumbers = linked };
            payers[(billerId, payerId)] = updated;
            return Task.FromResult(updated);
        }
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

    private bool IsAccountLinked(string billerId, string accountNumber, string? payerId)
        => payers.Values.Any(existing => existing.BillerId == billerId
            && existing.PayerId != payerId
            && existing.AccountNumbers.Contains(accountNumber, StringComparer.Ordinal));

    private static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();

    private static List<string> NormalizeAccounts(IReadOnlyList<string> accounts) => accounts
        .Select(account => account.Trim())
        .Where(account => account.Length > 0)
        .Distinct(StringComparer.Ordinal)
        .ToList();
}
