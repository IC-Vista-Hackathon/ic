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
        var capability = ValidateCapability(ServiceToolRegistry.ToolNames.GetBillerConfiguration, capabilityToken);
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
        var capability = ValidateCapability(ServiceToolRegistry.ToolNames.ListInvoices, capabilityToken);
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
        var capability = ValidateCapability(ServiceToolRegistry.ToolNames.GetInvoice, capabilityToken);
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
        var capability = ValidateCapability(ServiceToolRegistry.ToolNames.GetPaymentQuote, capabilityToken);
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
        var capability = ValidateCapability(ServiceToolRegistry.ToolNames.VerifyPayerAccount, capabilityToken);
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
        var capability = ValidateCapability(ServiceToolRegistry.ToolNames.GetPayerProfile, capabilityToken, payerRequired: true);
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
        var capability = ValidateCapability(ServiceToolRegistry.ToolNames.GetPaymentHistory, capabilityToken, payerRequired: true);
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
        var capability = ValidateCapability(
            ServiceToolRegistry.ToolNames.UpdatePayerPreferences,
            capabilityToken,
            writeRequired: true,
            payerRequired: true);
        return await InvokeAsync(ServiceToolRegistry.ToolNames.UpdatePayerPreferences, capability, () =>
            payers.UpdatePreferencesAsync(
                capability.BillerId,
                capability.PayerId!,
                new UpdatePayerPreferencesRequest(autopay, paperless, Channels: null, paymentDay),
                cancellationToken).AsTask());
    }

    [McpServerTool(Name = ServiceToolRegistry.ToolNames.BindExecutionCapability, ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Re-issue a verified, write-capable payer capability as an Execution-bound capability at the Policy->Execution handoff. The biller, run, payer binding, and write scope are preserved from the presented capability; only the agent id is rebound to the Execution Agent. No money moves — the returned capability is what create_payment_intent/submit_payment require for a registered payer.")]
    public async ValueTask<ExecutionCapabilityResult> BindExecutionCapabilityAsync(
        [Description("Write-capable payer-bound capability returned by verify_payer_account.")] string capabilityToken,
        CancellationToken cancellationToken)
    {
        // The presented capability must already be a verified, write-capable payer capability; the
        // payer id and tenant are trusted from the signed token, never a tool argument. The re-issued
        // token is bound to the configured Execution Agent so the server-side Execution-Agent-only
        // submission rule holds without ever weakening it or exposing tokens across the model boundary.
        var capability = ValidateCapability(
            ServiceToolRegistry.ToolNames.BindExecutionCapability,
            capabilityToken,
            writeRequired: true,
            payerRequired: true);
        return await InvokeAsync(ServiceToolRegistry.ToolNames.BindExecutionCapability, capability, () =>
        {
            var executionToken = capabilities.Issue(
                capability.BillerId, capability.RunId, executionAgentId, capability.CanWrite, capability.PayerId,
                notAfter: capability.ExpiresAt);
            return Task.FromResult(new ExecutionCapabilityResult(executionToken));
        });
    }

    [McpServerTool(Name = ServiceToolRegistry.ToolNames.CreatePaymentIntent, ReadOnly = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Quote a payment and return a confirmation-required intent (carrying an idempotency key) to approve. Requires a write-capable capability. When the capability is payer-bound the intent is for that payer; a biller-scoped capability produces a guest intent (no payer account). No money moves; submit_payment executes the approved intent.")]
    public async ValueTask<PaymentIntentView> CreatePaymentIntentAsync(
        [Description("Write-capable capability: an Execution-bound payer capability for a registered payer, or a biller capability for guest pay.")] string capabilityToken,
        [Description("The invoice id to pay.")] string invoiceId,
        [Description("The payment method token (must be enabled for the biller).")] string method,
        [Description("Optional future date (yyyy-MM-dd) to schedule the payment; omit to pay now.")] DateOnly? scheduledFor,
        CancellationToken cancellationToken)
    {
        // Guest pay is biller-scoped: a write capability is always required, but a payer binding is
        // optional. A payer-bound capability produces an intent for that payer; a biller capability
        // produces a guest intent with no payer account. Identity still comes only from the token.
        var capability = ValidateCapability(
            ServiceToolRegistry.ToolNames.CreatePaymentIntent,
            capabilityToken,
            writeRequired: true);
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
    [Description("Submit an approved payment intent, moving money via the idempotent Payment Service. Restricted to the Execution Agent and only after explicit payer confirmation. Requires a write-capable capability; pays as the bound payer, or as a biller-scoped guest when the capability carries no payer. Resubmitting the same intent id resolves to the original payment (no double charge).")]
    public async ValueTask<PaymentResponse> SubmitPaymentAsync(
        [Description("Write-capable capability: an Execution-bound payer capability for a registered payer, or a biller capability for guest pay.")] string capabilityToken,
        [Description("The intent id returned by create_payment_intent; used as the idempotency key so retries never double-charge.")] string intentId,
        [Description("The invoice id from the approved intent.")] string invoiceId,
        [Description("The payment method from the approved intent.")] string method,
        [Description("Must be true: the payer has explicitly confirmed this payment. The server refuses to submit otherwise.")] bool payerConfirmed,
        [Description("Optional schedule date from the approved intent; omit to pay now.")] DateOnly? scheduledFor,
        CancellationToken cancellationToken)
    {
        // Guest pay is biller-scoped: the Execution-Agent-only and payer-confirmation gates below
        // are unconditional, but the payer binding is optional. capability.PayerId flows through as
        // the payer account (null for a guest), so the Payment Service records a guest payment.
        var capability = ValidateCapability(
            ServiceToolRegistry.ToolNames.SubmitPayment,
            capabilityToken,
            writeRequired: true);
        return await InvokeAsync(ServiceToolRegistry.ToolNames.SubmitPayment, capability, async () =>
        {
            if (!string.Equals(capability.AgentId, executionAgentId, StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException("Only the Execution Agent may submit a payment.");
            }

            if (!payerConfirmed)
            {
                throw new UnauthorizedAccessException("A payment can only be submitted after explicit payer confirmation.");
            }

            return await payments.CreateAsync(
                new CreatePaymentRequest(
                    capability.BillerId,
                    RequireArgument(invoiceId, nameof(invoiceId)),
                    RequireArgument(method, nameof(method)),
                    capability.PayerId,
                    scheduledFor),
                RequireArgument(intentId, nameof(intentId)),
                cancellationToken);
        });
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
        var capability = ValidateCapability(
            ServiceToolRegistry.ToolNames.SeedInvoices,
            capabilityToken,
            writeRequired: true);
        return await InvokeAsync(ServiceToolRegistry.ToolNames.SeedInvoices, capability, async () =>
        {
            if (!maintenance.Value.SeedingEnabled)
            {
                throw new InvalidOperationException("The seed_invoices tool is not available in this environment.");
            }

            return await invoices.SeedAsync(
                capability.BillerId,
                new SeedInvoicesRequest(count, accountNumber, billType),
                cancellationToken);
        });
    }

    [McpServerTool(Name = ServiceToolRegistry.ToolNames.RegisterPayer, ReadOnly = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Register a payer account for the capability's biller after explicit payer opt-in. Requires a write-capable biller capability. Registration is offered, never imposed — guest pay must remain available. The biller is bound from the capability, never an argument.")]
    public async ValueTask<PayerResponse> RegisterPayerAsync(
        [Description("Write-capable biller capability issued by IC orchestration.")] string capabilityToken,
        [Description("The payer's full name.")] string name,
        [Description("The payer's email address.")] string email,
        [Description("The payer's phone number. Omit if not provided.")] string? phone,
        [Description("External biller account numbers to link to this payer.")] IReadOnlyList<string> accountNumbers,
        [Description("Enable autopay. Omit to leave preferences at registration defaults.")] bool? autopay,
        [Description("Enable paperless billing. Omit to leave preferences at registration defaults.")] bool? paperless,
        [Description("Day of month (1-28) to run autopay. Omit to leave unset.")] int? paymentDay,
        CancellationToken cancellationToken)
    {
        var capability = ValidateCapability(
            ServiceToolRegistry.ToolNames.RegisterPayer,
            capabilityToken,
            writeRequired: true);
        return await InvokeAsync(ServiceToolRegistry.ToolNames.RegisterPayer, capability, () =>
        {
            PayerPreferences? preferences = autopay is null && paperless is null && paymentDay is null
                ? null
                : new PayerPreferences(autopay ?? false, paperless ?? false, Channels: [], paymentDay);
            return payers.RegisterAsync(
                new RegisterPayerRequest(
                    capability.BillerId,
                    RequireArgument(name, nameof(name)),
                    RequireArgument(email, nameof(email)),
                    string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
                    accountNumbers ?? [],
                    preferences),
                cancellationToken).AsTask();
        });
    }

    private async ValueTask<T> InvokeAsync<T>(string toolName, AgentContextCapability capability, Func<Task<T>> action)
    {
        using var activity = McpTelemetry.StartToolActivity(toolName, capability);
        var startedAt = Stopwatch.GetTimestamp();
        McpTelemetry.RecordInvoked(toolName, capability, activity);
        try
        {
            var result = await action();
            McpTelemetry.RecordCompleted(
                toolName, capability, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, activity);
            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var (category, statusCode) = McpTelemetry.Categorize(exception);
            McpTelemetry.RecordFailed(
                toolName, capability, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                category, statusCode, activity);
            LogToolFailed(
                logger, toolName, capability.BillerId, capability.AgentId, category, statusCode,
                Activity.Current?.TraceId.ToString());
            throw;
        }
    }

    private AgentContextCapability ValidateCapability(
        string toolName, string capabilityToken, bool writeRequired = false, bool payerRequired = false)
    {
        try
        {
            return capabilities.Validate(capabilityToken, writeRequired, payerRequired);
        }
        catch (UnauthorizedAccessException)
        {
            McpTelemetry.RecordDenied(toolName);
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

    [LoggerMessage(2801, LogLevel.Error, "MCP service tool {ToolName} failed for biller {BillerId}, agent {AgentId}; category {FailureCategory}, status {StatusCode}, trace {TraceId}")]
    private static partial void LogToolFailed(
        ILogger logger, string toolName, string billerId, string agentId,
        string failureCategory, int statusCode, string? traceId);
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

/// <summary>
/// An Execution-bound re-issue of a verified payer capability, produced at the Policy->Execution
/// handoff. It preserves the biller/run/payer scope and write flag of the presented capability and
/// binds the agent id to the Execution Agent so the payment path's Execution-Agent-only rule holds.
/// </summary>
public sealed record ExecutionCapabilityResult(string ExecutionCapabilityToken);

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
