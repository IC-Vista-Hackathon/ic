namespace Pronto.PayerAccount.Api.Accounts;

/// <summary>
/// Decides whether a biller account number may be linked to a payer. Account numbers are
/// external identifiers (not references we own), so ownership is established out-of-band by
/// asking the authoritative service. Injected so the linking policy can be swapped (HTTP-backed
/// Invoice lookup in production, a fake in tests) without touching the controller or stores.
/// </summary>
public interface IAccountOwnershipVerifier
{
    /// <summary>
    /// True when <paramref name="accountNumber"/> is a real account under <paramref name="billerId"/>
    /// and may be linked. Implementations must be side-effect free.
    /// </summary>
    Task<bool> IsOwnedAsync(string billerId, string accountNumber, CancellationToken cancellationToken = default);
}
