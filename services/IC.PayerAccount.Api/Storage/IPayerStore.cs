using IC.PayerAccount.Contracts.V1.Payers;
using IC.ServiceDefaults.Errors;

namespace IC.PayerAccount.Api.Storage;

public interface IPayerStore
{
    /// <summary>Adds the payer; throws 409 already_registered on duplicate email per biller.</summary>
    void Add(PayerResponse payer);

    PayerResponse? Find(string billerId, string payerId);
    PayerResponse? FindByAccount(string billerId, string accountNumber);

    void Update(PayerResponse payer);
}

public sealed class InMemoryPayerStore : IPayerStore
{
    private readonly object gate = new();
    private readonly Dictionary<(string BillerId, string PayerId), PayerResponse> payers = [];

    public void Add(PayerResponse payer)
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
    }

    public PayerResponse? Find(string billerId, string payerId)
    {
        lock (gate)
        {
            return payers.GetValueOrDefault((billerId, payerId));
        }
    }

    public PayerResponse? FindByAccount(string billerId, string accountNumber)
    {
        lock (gate)
        {
            return payers.Values.FirstOrDefault(payer => payer.BillerId == billerId
                && payer.AccountNumbers.Contains(accountNumber, StringComparer.Ordinal));
        }
    }

    public void Update(PayerResponse payer)
    {
        lock (gate)
        {
            payers[(payer.BillerId, payer.PayerId)] = payer;
        }
    }

    private static string Normalize(string email) => email.Trim().ToUpperInvariant();
}
