namespace Pronto.BillerExperience.Api.Infrastructure.Mcp;

/// <summary>
/// The scope a capability token must satisfy for a tool. Biller-scoped tools only need a valid
/// biller capability; payer-scoped tools additionally require a payer id bound by the
/// verification handshake.
/// </summary>
public enum ToolScope
{
    Biller,
    Payer,
}

/// <summary>
/// Declarative metadata for one MCP service tool. Mirrors the <c>[McpServerTool]</c> attributes
/// on the tool method so the router can reason about tools uniformly (authorization, telemetry,
/// documentation) without reflecting over the transport. Kept in one place so PR C telemetry and
/// tests assert against a single source of truth.
/// </summary>
public sealed record ServiceToolDescriptor(
    string Name,
    string Title,
    bool ReadOnly,
    bool Idempotent,
    ToolScope Scope,
    bool WriteCapabilityRequired,
    string Summary);

/// <summary>The canonical set of MCP service tools and their contract metadata.</summary>
public sealed class ServiceToolRegistry
{
    public static class ToolNames
    {
        public const string GetBillerConfiguration = "get_biller_configuration";
        public const string ListInvoices = "list_invoices";
        public const string GetInvoice = "get_invoice";
        public const string GetPaymentQuote = "get_payment_quote";
        public const string VerifyPayerAccount = "verify_payer_account";
        public const string GetPayerProfile = "get_payer_profile";
        public const string GetPaymentHistory = "get_payment_history";
        public const string UpdatePayerPreferences = "update_payer_preferences";
        public const string CreatePaymentIntent = "create_payment_intent";
        public const string SubmitPayment = "submit_payment";
        public const string SeedInvoices = "seed_invoices";
    }

    private static readonly IReadOnlyList<ServiceToolDescriptor> Descriptors =
    [
        new(ToolNames.GetBillerConfiguration, "Get biller configuration", ReadOnly: true, Idempotent: true,
            ToolScope.Biller, WriteCapabilityRequired: false,
            "Reads the biller's current experience configuration (brand, enabled payment methods, fee handling, revision state)."),
        new(ToolNames.ListInvoices, "List invoices", ReadOnly: true, Idempotent: true,
            ToolScope.Biller, WriteCapabilityRequired: false,
            "Lists a payer account's invoices for the capability's biller, looked up by account number."),
        new(ToolNames.GetInvoice, "Get invoice", ReadOnly: true, Idempotent: true,
            ToolScope.Biller, WriteCapabilityRequired: false,
            "Reads one invoice by id for the capability's biller."),
        new(ToolNames.GetPaymentQuote, "Get payment quote", ReadOnly: true, Idempotent: true,
            ToolScope.Biller, WriteCapabilityRequired: false,
            "Returns the pre-confirmation fee quote for an invoice + payment method; the total matches what a payment would charge."),
        new(ToolNames.VerifyPayerAccount, "Verify payer account", ReadOnly: true, Idempotent: true,
            ToolScope.Biller, WriteCapabilityRequired: false,
            "Matches an account number to a registered payer and, on success, issues a payer-bound capability token used by payer-scoped tools."),
        new(ToolNames.GetPayerProfile, "Get payer profile", ReadOnly: true, Idempotent: true,
            ToolScope.Payer, WriteCapabilityRequired: false,
            "Reads the verified payer's profile and preferences. Requires a payer-bound capability."),
        new(ToolNames.GetPaymentHistory, "Get payment history", ReadOnly: true, Idempotent: true,
            ToolScope.Payer, WriteCapabilityRequired: false,
            "Lists the verified payer's payment history. Requires a payer-bound capability."),
        new(ToolNames.UpdatePayerPreferences, "Update payer preferences", ReadOnly: false, Idempotent: true,
            ToolScope.Payer, WriteCapabilityRequired: true,
            "Updates the verified payer's notification/autopay preferences. Requires a write-capable, payer-bound capability."),
        new(ToolNames.CreatePaymentIntent, "Create payment intent", ReadOnly: true, Idempotent: true,
            ToolScope.Payer, WriteCapabilityRequired: true,
            "Quotes a payment and returns a confirmation-required intent (with an idempotency key) for the payer to approve. Requires a write-capable, payer-bound capability. No money moves."),
        new(ToolNames.SubmitPayment, "Submit payment", ReadOnly: false, Idempotent: true,
            ToolScope.Payer, WriteCapabilityRequired: true,
            "Submits a confirmed payment intent (money moves) via the idempotent Payment Service. Execution-Agent-only and requires explicit payer confirmation plus a write-capable, payer-bound capability."),
        new(ToolNames.SeedInvoices, "Seed invoices", ReadOnly: false, Idempotent: false,
            ToolScope.Biller, WriteCapabilityRequired: true,
            "Seeds fake demo invoices for the biller. Nonprod/demo-gated; requires a write-capable biller capability."),
    ];

    public IReadOnlyList<ServiceToolDescriptor> All => Descriptors;

    public ServiceToolDescriptor Get(string name) =>
        Descriptors.FirstOrDefault(descriptor => descriptor.Name == name)
            ?? throw new KeyNotFoundException($"No MCP service tool is registered under the name '{name}'.");
}
