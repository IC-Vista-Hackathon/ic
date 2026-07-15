using System.Security.Claims;
using Pronto.ServiceDefaults.Errors;

namespace Pronto.ServiceDefaults.Security;

/// <summary>
/// Tenant enforcement helper. Every partition-scoped request carries a <c>biller_id</c>; a
/// caller may only act for the biller its token is scoped to, unless it holds the
/// cross-biller service scope (a peer service that legitimately spans tenants). Guards
/// against a valid token for biller A being replayed against biller B's data or money.
/// </summary>
public static class BillerClaims
{
    /// <summary>
    /// Throws when <paramref name="user"/> is not authorized to act for <paramref name="billerId"/>.
    /// A blank <paramref name="billerId"/> is a client error (400); a mismatched tenant is 403.
    /// </summary>
    public static void RequireBillerAccess(ClaimsPrincipal user, string? billerId)
    {
        if (string.IsNullOrWhiteSpace(billerId))
        {
            throw ServiceException.BadRequest("invalid_biller", "biller_id is required.");
        }

        if (user.HasGrant(ServiceClaims.CrossBillerRole))
        {
            return;
        }

        var scoped = user.FindFirst(ServiceClaims.BillerId)?.Value;
        if (string.IsNullOrEmpty(scoped) || !string.Equals(scoped, billerId.Trim(), StringComparison.Ordinal))
        {
            throw ServiceException.Forbidden(
                "biller_forbidden", "caller is not authorized to act for this biller.");
        }
    }
}
