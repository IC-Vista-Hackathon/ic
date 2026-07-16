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

    /// <summary>
    /// Payer registration and preference operations. Held by the Policy Agent (payer-facing
    /// registration), tenant-scoped to the caller's biller.
    /// </summary>
    public const string PayersWrite = "payers:write";

    /// <summary>
    /// Internal onboarding seam — <c>POST /payers/seed</c>. Seeds the demo payer during onboarding,
    /// the payer-side parallel to <see cref="InvoiceSeed"/>; kept distinct from <see cref="PayersWrite"/>
    /// so seeding does not widen the payer-facing registration policy.
    /// </summary>
    public const string PayersSeed = "payers:seed";

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
        options.AddPolicy(PayersSeed, RolePolicy(ServiceClaims.PayerSeedRole, ServiceClaims.CrossBillerRole));
        options.AddPolicy(Maintenance, RolePolicy(ServiceClaims.MaintenanceRole));
    }

    /// <summary>Policy satisfied when the caller holds any one of <paramref name="acceptedRoles"/>.</summary>
    private static AuthorizationPolicy RolePolicy(params string[] acceptedRoles) =>
        new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireAssertion(context => Array.Exists(acceptedRoles, context.User.HasGrant))
            .Build();
}
