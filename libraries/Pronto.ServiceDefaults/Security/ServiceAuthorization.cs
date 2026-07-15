using Microsoft.AspNetCore.Authorization;

namespace Pronto.ServiceDefaults.Security;

/// <summary>
/// Named authorization policies for the documented capability boundaries. Controllers apply
/// these with <c>[Authorize(Policy = ...)]</c>; every policy also requires an authenticated
/// caller, and the global fallback policy denies any endpoint that doesn't opt out.
/// </summary>
public static class ServiceAuthorization
{
    /// <summary>Execution Agent — <c>POST /payments</c>.</summary>
    public const string PaymentsWrite = "payments:write";

    public const string PurchasesWrite = "purchases:write";

    public const string BillerPurchaseWrite = "billers:purchase";

    /// <summary>Payment Service — <c>POST /billers/{id}/invoices/{invoiceId}/status</c>.</summary>
    public const string InvoiceStatusWrite = "invoices:status";

    /// <summary>Internal onboarding seam — <c>POST /billers/{id}/invoices/seed</c>.</summary>
    public const string InvoiceSeed = "invoices:seed";

    /// <summary>Policy Agent — payer registration and preference operations.</summary>
    public const string PayersWrite = "payers:write";

    /// <summary>Test-data maintenance endpoints (nonprod).</summary>
    public const string Maintenance = "maintenance";

    public static void AddServicePolicies(this AuthorizationOptions options)
    {
        options.AddPolicy(PaymentsWrite, RolePolicy(ServiceClaims.ExecutionAgentRole));
        options.AddPolicy(
            PurchasesWrite,
            RolePolicy(ServiceClaims.OnboardingAgentRole, ServiceClaims.BillerExperienceServiceRole));
        options.AddPolicy(BillerPurchaseWrite, RolePolicy(ServiceClaims.PaymentServiceRole));
        options.AddPolicy(InvoiceStatusWrite, RolePolicy(ServiceClaims.PaymentServiceRole));
        options.AddPolicy(InvoiceSeed, RolePolicy(ServiceClaims.InvoiceSeedRole, ServiceClaims.CrossBillerRole));
        options.AddPolicy(PayersWrite, RolePolicy(ServiceClaims.PolicyAgentRole));
        options.AddPolicy(Maintenance, RolePolicy(ServiceClaims.MaintenanceRole));
    }

    /// <summary>Policy satisfied when the caller holds any one of <paramref name="acceptedRoles"/>.</summary>
    private static AuthorizationPolicy RolePolicy(params string[] acceptedRoles) =>
        new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireAssertion(context => Array.Exists(acceptedRoles, context.User.HasGrant))
            .Build();
}
