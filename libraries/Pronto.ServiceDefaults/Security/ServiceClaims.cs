using System.Security.Claims;

namespace Pronto.ServiceDefaults.Security;

/// <summary>
/// Claim types and Entra <b>app roles</b> Pronto services agree on. Roles model the documented
/// agent/service boundaries (design/services.md, design/contracts.md): each capability that
/// mutates data or moves money is gated by a distinct role, so a token minted for one agent or
/// peer service cannot exercise another's authority. App roles surface in the <c>roles</c>
/// claim; delegated <c>scope</c>/<c>scp</c> claims are also honoured so the same policies work
/// if a capability is ever exposed as a delegated permission.
/// </summary>
public static class ServiceClaims
{
    /// <summary>Tenant claim: the biller a caller is scoped to act for.</summary>
    public const string BillerId = "biller_id";

    /// <summary>Claim types that carry an app role or scope grant.</summary>
    public static readonly string[] GrantClaimTypes =
    [
        "roles",
        ClaimTypes.Role,
        "scope",
        "scp",
        "http://schemas.microsoft.com/identity/claims/scope",
    ];

    // App roles, aligned with the documented service/agent boundaries.

    /// <summary>Execution Agent — the only caller allowed to move money via <c>POST /payments</c>.</summary>
    public const string ExecutionAgentRole = "agent.execution";

    /// <summary>Policy Agent — owns payer registration and preference operations.</summary>
    public const string PolicyAgentRole = "agent.policy";

    public const string OnboardingAgentRole = "agent.onboarding";

    /// <summary>Payment Service — asserts invoice status transitions (<c>due→paid/scheduled</c>).</summary>
    public const string PaymentServiceRole = "service.payment";

    public const string BillerExperienceServiceRole = "service.biller-experience";

    /// <summary>Internal onboarding seam that seeds fake invoices.</summary>
    public const string InvoiceSeedRole = "service.invoice-seed";

    /// <summary>Test-data maintenance role (nonprod cleanup endpoints).</summary>
    public const string MaintenanceRole = "maintenance";

    /// <summary>
    /// Cross-tenant authority held by peer services that legitimately span billers (they carry
    /// no single-biller claim). Agent principals do <em>not</em> hold this — they are pinned to a
    /// <see cref="BillerId"/> claim and validated against the request's biller.
    /// </summary>
    public const string CrossBillerRole = "service.cross-biller";

    /// <summary>
    /// Every role an unrestricted service principal holds. Used only by the local/test scheme's
    /// default identity so local runs and existing tests work without hand-minting a token; never
    /// granted implicitly in production.
    /// </summary>
    public static readonly string[] AllRoles =
    [
        ExecutionAgentRole,
        PolicyAgentRole,
        OnboardingAgentRole,
        PaymentServiceRole,
        BillerExperienceServiceRole,
        InvoiceSeedRole,
        MaintenanceRole,
        CrossBillerRole,
    ];

    /// <summary>True when the principal holds <paramref name="grant"/> as an app role or scope.</summary>
    public static bool HasGrant(this ClaimsPrincipal principal, string grant)
    {
        foreach (var claimType in GrantClaimTypes)
        {
            foreach (var claim in principal.FindAll(claimType))
            {
                // OAuth space-delimits multiple scopes in one claim; role claims are singular.
                foreach (var token in claim.Value.Split(
                    ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (string.Equals(token, grant, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }
}
