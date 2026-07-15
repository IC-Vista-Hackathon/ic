using System.Security.Cryptography;
using System.Text;

namespace Pronto.Payment.Api.Purchases;

/// <summary>
/// Deterministic document identity for the one purchase allowed in each biller partition.
/// Concurrent creates therefore target the same Cosmos item and are resolved atomically by
/// <c>CreateItemAsync</c>, without a query-then-insert race.
/// </summary>
public static class PurchaseIdentity
{
    public static string ForBiller(string billerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(billerId);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"pronto-purchase:{billerId}"));
        return $"purchase-{Convert.ToHexStringLower(hash)}";
    }
}
