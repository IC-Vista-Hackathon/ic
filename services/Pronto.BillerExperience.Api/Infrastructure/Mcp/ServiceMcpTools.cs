using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using Pronto.BillerExperience.Api.Application;
using Pronto.BillerExperience.Api.Configuration;
using Pronto.BillerExperience.Api.Infrastructure.Mcp.ServiceClients;
using Pronto.BillerExperience.Contracts.V1.Experiences;
using Pronto.Invoice.Contracts.V1.Invoices;
using Pronto.Payment.Contracts.V1.Payments;
using Pronto.PayerAccount.Contracts.V1.Payers;

namespace Pronto.BillerExperience.Api.Infrastructure.Mcp;

/// <summary>
/// Deterministic MCP service router. Every tool validates a capability token first (identity is
/// bound to the token, never taken from a tool argument), then calls a typed downstream client.
/// Read-only reads, the payer-verification handshake, and controlled writes all live here; each
/// write is gated — write-capable capability, payer binding, explicit payer confirmation, and the
/// Execution-Agent-only rule for payment submission. The MCP never mutates payment/invoice state
/// directly; it delegates to the (idempotent) Payment Service, which owns those transitions.
/// </summary>
[McpServerToolType]
public sealed partial class ServiceMcpTools(
    AgentContextCapabilityService capabilities,
    BillerOnboardingService onboarding,
    IInvoiceServiceClient invoices,
    IPaymentServiceClient payments,
    IPayerAccountServiceClient payers,
    IOptions<BillerExperienceOptions> options,
    IOptions<MaintenanceOptions> maintenance,
    ILogger<ServiceMcpTools> logger)
{
    private readonly string executionAgentId = options.Value.Mcp.ExecutionAgentId;

    [McpServerTool(Name = ServiceToolRegistry.ToolNames.GetBillerConfiguration, ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Read the current experience configuration for the capability's biller: brand, enabled payment methods, fee handling, and revision state.")]
    public async ValueTask<BillerConfigurationView> GetBillerConfigurationAsync(
        [Description("Short-lived biller capability issued by IC orchestration.")] string capabilityToken,
        CancellationToken cancellationToken)
    {
        var capability = capabilities.Validate(capabilityToken, writeRequired: false);
        return await InvokeAsync(ServiceToolRegistry.ToolNames.GetBillerConfiguration, capability, async () =>
        {
            var revision = await onboarding.GetDraftAsync(capability.BillerId, cancellationToken);
            return BillerConfigurationView.From(revision);
        });
    }

    [McpServerTool(Name = ServiceToolRegistry.ToolNames.ListInvoices, ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("List a payer account's invoices for the capability's biller, looked up by account number.")]
    public async ValueTask<InvoiceListResponse> ListInvoicesAsync(
        [Description("Short-lived biller capability issued by IC orchestration.")] string capabilityToken,
        [Description("The payer's account number to look up invoices for.")] string accountNumber,
        [Description("Include paid/closed invoices in addition to open ones.")] bool includeClosed,
        CancellationToken cancellationToken)
    {
        var capability = capabilities.Validate(capabilityToken, writeRequired: false);
        return await InvokeAsync(ServiceToolRegistry.ToolNames.ListInvoices, capability, () =>
            invoices.ListAsync(capability.BillerId, RequireArgument(accountNumber, nameof(accountNumber)), includeClosed, cancellationToken).AsTask());
    }

    [McpServerTool(Name = ServiceToolRegistry.ToolNames.GetInvoice, ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Read one invoice by id for the capability's biller.")]
    public async ValueTask<InvoiceResponse> GetInvoiceAsync(
        [Description("Short-lived biller capability issued by IC orchestration.")] string capabilityToken,
        [Description("The invoice id to read.")] string invoiceId,
        CancellationToken cancellationToken)
    {
        var capability = capabilities.Validate(capabilityToken, writeRequired: false);
        return await InvokeAsync(ServiceToolRegistry.ToolNames.GetInvoice, capability, async () =>
            await invoices.GetAsync(capability.BillerId, RequireArgument(invoiceId, nameof(invoiceId)), cancellationToken)
                ?? throw new KeyNotFoundException($"Invoice {invoiceId} was not found for this biller."));
    }

    [McpServerTool(Name = ServiceToolRegistry.ToolNames.GetPaymentQuote, ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Return the pre-confirmation fee quote for an invoice and payment method; the quoted total matches what a payment would charge.")]
    public async ValueTask<PaymentQuoteResponse> GetPaymentQuoteAsync(
        [Description("Short-lived biller capability issued by IC orchestration.")] string capabilityToken,
        [Description("The invoice id to quote.")] string invoiceId,
        [Description("The payment method token (must be enabled for the biller).")] string method,
        CancellationToken cancellationToken)
    {
        var capability = capabilities.Validate(capabilityToken, writeRequired: false);
        return await InvokeAsync(ServiceToolRegistry.ToolNames.GetPaymentQuote, capability, () =>
            payments.GetQuoteAsync(
                capability.BillerId,
                RequireArgument(invoiceId, nameof(invoiceId)),
                RequireArgument(method, nameof(method)),
                cancellationToken).AsTask());
    }

    [McpServerTool(Name = ServiceToolRegistry.ToolNames.VerifyPayerAccount, ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Match an account number to a registered payer for the capability's biller. On success, returns a payer-bound capability token that payer-scoped tools require. The payer id is bound server-side; it can never be passed as an argument.")]
    public async ValueTask<PayerVerificationResult> VerifyPayerAccountAsync(
        [Description("Short-lived biller capability issued by IC orchestration.")] string capabilityToken,
        [Description("The account number the payer supplied to prove ownership.")] string accountNumber,
        CancellationToken cancellationToken)
    {
        var capability = capabilities.Validate(capabilityToken, writeRequired: false);
        return await InvokeAsync(ServiceToolRegistry.ToolNames.VerifyPayerAccount, capability, async () =>
        {
            var payer = await payers.FindByAccountAsync(capability.BillerId, RequireArgument(accountNumber, nameof(accountNumber)), cancellationToken);
            if (payer is null || !payer.AccountNumbers.Contains(accountNumber.Trim()))
            {
                // Uniform failure — never reveal whether the account exists.
                throw new UnauthorizedAccessException("Payer account verification failed.");
            }

            var payerToken = capabilities.Issue(
                capability.BillerId, capability.RunId, capability.AgentId, capability.CanWrite, payer.PayerId);
            LogPayerVerified(logger, capability.BillerId, payer.PayerId, capability.AgentId, Activity.Current?.TraceId.ToString());
            return new PayerVerificationResult(payer.PayerId, payer.Name, payerToken);
        });
    }

    [McpServerTool(Name = ServiceToolRegistry.ToolNames.GetPayerProfile, ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Read the verified payer's profile and preferences. Requires a payer-bound capability from verify_payer_account.")]
    public async ValueTask<PayerResponse> GetPayerProfileAsync(
        [Description("Payer-bound capability returned by verify_payer_account.")] string capabilityToken,
        CancellationToken cancellationToken)
    {
        var capability = capabilities.Validate(capabilityToken, writeRequired: false, payerRequired: true);
        return await InvokeAsync(ServiceToolRegistry.ToolNames.GetPayerProfile, capability, async () =>
            await payers.GetAsync(capability.BillerId, capability.PayerId!, cancellationToken)
                ?? throw new KeyNotFoundException("The verified payer profile was not found."));
    }

    [McpServerTool(Name = ServiceToolRegistry.ToolNames.GetPaymentHistory, ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("List the verified payer's payment history. Requires a payer-bound capability from verify_payer_account.")]
    public async ValueTask<PaymentHistoryView> GetPaymentHistoryAsync(
        [Description("Payer-bound capability returned by verify_payer_account.")] string capabilityToken,
        CancellationToken cancellationToken)
    {
        var capability = capabilities.Validate(capabilityToken, writeRequired: false, payerRequired: true);
        return await InvokeAsync(ServiceToolRegistry.ToolNames.GetPaymentHistory, capability, async () =>
        {
            var history = await payments.ListAsync(capability.BillerId, capability.PayerId!, cancellationToken);
            return new PaymentHistoryView(history);
        });
    }

    [McpServerTool(Name = ServiceToolRegistry.ToolNames.UpdatePayerPreferences, ReadOnly = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Update the verified payer's notification/autopay preferences. Requires a write-capable, payer-bound capability from verify_payer_account. Only supplied fields change; omit a field to leave it unchanged.")]
    public async ValueTask<PayerPreferences> UpdatePayerPreferencesAsync(
        [Description("Write-capable payer-bound capability returned by verify_payer_account.")] string capabilityToken,
        [Description("Enable/disable autopay. Omit to leave unchanged. Enabling autopay requires a payment day set now or already on file.")] bool? autopay,
        [Description("Enable/disable paperless billing. Omit to leave unchanged.")] bool? paperless,
        [Description("Day of month (1-28) to run autopay. Omit to leave unchanged.")] int? paymentDay,
        CancellationToken cancellationToken)
    {
        var capability = capabilities.Validate(capabilityToken, writeRequired: true, payerRequired: true);
        return await InvokeAsync(ServiceToolRegistry.ToolNames.UpdatePayerPreferences, capability, () =>
            payers.UpdatePreferencesAsync(
                capability.BillerId,
                capability.PayerId!,
                new UpdatePayerPreferencesRequest(autopay, paperless, Channels: null, paymentDay),
                cancellationToken).AsTask());
    }

    [McpServerTool(Name = ServiceToolRegistry.ToolNames.CreatePaymentIntent, ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Quote a payment for the verified payer and return a confirmation-required intent (carrying an idempotency key) for them to approve. Requires a write-capable, payer-bound capability. No money moves; submit_payment executes the approved intent.")]
    public async ValueTask<PaymentIntentView> CreatePaymentIntentAsync(
        [Description("Write-capable payer-bound capability returned by verify_payer_account.")] string capabilityToken,
        [Description("The invoice id to pay.")] string invoiceId,
        [Description("The payment method token (must be enabled for the biller).")] string method,
        [Description("Optional future date (yyyy-MM-dd) to schedule the payment; omit to pay now.")] DateOnly? scheduledFor,
        CancellationToken cancellationToken)
    {
        var capability = capabilities.Validate(capabilityToken, writeRequired: true, payerRequired: true);
        return await InvokeAsync(ServiceToolRegistry.ToolNames.CreatePaymentIntent, capability, async () =>
        {
            var requiredInvoiceId = RequireArgument(invoiceId, nameof(invoiceId));
            var requiredMethod = RequireArgument(method, nameof(method));
            var quote = await payments.GetQuoteAsync(capability.BillerId, requiredInvoiceId, requiredMethod, cancellationToken);
            return new PaymentIntentView(
                IntentId: Guid.NewGuid().ToString(),
                InvoiceId: requiredInvoiceId,
                Method: requiredMethod,
                PayerAccountId: capability.PayerId,
                AmountCents: quote.AmountCents,
                FeeCents: quote.FeeCents,
                TotalCents: quote.TotalCents,
                ScheduledFor: scheduledFor,
                Status: "requires_confirmation");
        });
    }

    [McpServerTool(Name = ServiceToolRegistry.ToolNames.SubmitPayment, ReadOnly = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Submit an approved payment intent, moving money via the idempotent Payment Service. Restricted to the Execution Agent and only after explicit payer confirmation. Requires a write-capable, payer-bound capability. Resubmitting the same intent id resolves to the original payment (no double charge).")]
    public async ValueTask<PaymentResponse> SubmitPaymentAsync(
        [Description("Write-capable payer-bound capability returned by verify_payer_account.")] string capabilityToken,
        [Description("The intent id returned by create_payment_intent; used as the idempotency key so retries never double-charge.")] string intentId,
        [Description("The invoice id from the approved intent.")] string invoiceId,
        [Description("The payment method from the approved intent.")] string method,
        [Description("Must be true: the payer has explicitly confirmed this payment. The server refuses to submit otherwise.")] bool payerConfirmed,
        [Description("Optional schedule date from the approved intent; omit to pay now.")] DateOnly? scheduledFor,
        CancellationToken cancellationToken)
    {
        var capability = capabilities.Validate(capabilityToken, writeRequired: true, payerRequired: true);

        // Money movement is Execution-Agent-only (design/services.md), enforced server-side here.
        if (!string.Equals(capability.AgentId, executionAgentId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Only the Execution Agent may submit a payment.");
        }

        if (!payerConfirmed)
        {
            throw new UnauthorizedAccessException("A payment can only be submitted after explicit payer confirmation.");
        }

        return await InvokeAsync(ServiceToolRegistry.ToolNames.SubmitPayment, capability, () =>
            payments.CreateAsync(
                new CreatePaymentRequest(
                    capability.BillerId,
                    RequireArgument(invoiceId, nameof(invoiceId)),
                    RequireArgument(method, nameof(method)),
                    capability.PayerId,
                    scheduledFor),
                RequireArgument(intentId, nameof(intentId)),
                cancellationToken).AsTask());
    }

    [McpServerTool(Name = ServiceToolRegistry.ToolNames.SeedInvoices, ReadOnly = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Seed fake demo invoices for the capability's biller. Nonprod/demo only: reports unavailable when seeding is disabled. Requires a write-capable biller capability.")]
    public async ValueTask<SeedInvoicesResponse> SeedInvoicesAsync(
        [Description("Write-capable biller capability issued by IC orchestration.")] string capabilityToken,
        [Description("How many invoices to seed. Omit for a sensible demo default.")] int? count,
        [Description("Account number to attach the invoices to. Omit to let the service generate one.")] string? accountNumber,
        [Description("Optional bill type label for the seeded set.")] string? billType,
        CancellationToken cancellationToken)
    {
        var capability = capabilities.Validate(capabilityToken, writeRequired: true);
        if (!maintenance.Value.SeedingEnabled)
        {
            // Uniform "unavailable" — seeding is invisible in prod, same discipline as the REST gate.
            throw new InvalidOperationException("The seed_invoices tool is not available in this environment.");
        }

        return await InvokeAsync(ServiceToolRegistry.ToolNames.SeedInvoices, capability, () =>
            invoices.SeedAsync(
                capability.BillerId,
                new SeedInvoicesRequest(count, accountNumber, billType),
                cancellationToken).AsTask());
    }

    private async ValueTask<T> InvokeAsync<T>(string toolName, AgentContextCapability capability, Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogToolFailed(logger, toolName, capability.BillerId, capability.AgentId, Activity.Current?.TraceId.ToString(), exception);
            throw;
        }
    }

    private static string RequireArgument(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"The '{name}' argument is required.", name);
        return value.Trim();
    }

    [LoggerMessage(2800, LogLevel.Information, "MCP verify_payer_account matched payer {PayerId} for biller {BillerId}, agent {AgentId}; trace {TraceId}")]
    private static partial void LogPayerVerified(ILogger logger, string billerId, string payerId, string agentId, string? traceId);

    [LoggerMessage(2801, LogLevel.Error, "MCP service tool {ToolName} failed for biller {BillerId}, agent {AgentId}; trace {TraceId}")]
    private static partial void LogToolFailed(ILogger logger, string toolName, string billerId, string agentId, string? traceId, Exception exception);
}

/// <summary>Agent-facing projection of a biller's current experience configuration.</summary>
public sealed record BillerConfigurationView(
    string BillerId,
    string DisplayName,
    string Revision,
    ExperienceRevisionState State,
    IReadOnlyList<string> EnabledPaymentMethods,
    FeeHandling? FeeHandling)
{
    public static BillerConfigurationView From(ExperienceRevisionResponse revision) => new(
        revision.BillerId,
        revision.Definition.Brand.DisplayName,
        revision.Revision,
        revision.State,
        revision.Definition.EnabledPaymentCapabilities,
        revision.Definition.Preferences?.FeeHandling);
}

/// <summary>Result of the payer-verification handshake. The token binds the matched payer id.</summary>
public sealed record PayerVerificationResult(
    string PayerId,
    string Name,
    string PayerCapabilityToken);

/// <summary>Agent-facing projection of a payer's payment history.</summary>
public sealed record PaymentHistoryView(IReadOnlyList<PaymentResponse> Payments);

/// <summary>
/// A payment the verified payer must confirm before it executes. <see cref="IntentId"/> doubles as
/// the idempotency key passed to the Payment Service on submit, so a retried submission of the same
/// approved intent resolves to the original payment. Nothing is charged until submit_payment.
/// </summary>
public sealed record PaymentIntentView(
    string IntentId,
    string InvoiceId,
    string Method,
    string? PayerAccountId,
    int AmountCents,
    int FeeCents,
    int TotalCents,
    DateOnly? ScheduledFor,
    string Status);
