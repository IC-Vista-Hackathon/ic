using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pronto.BillerExperience.Api;
using Pronto.BillerExperience.Api.Configuration;
using Pronto.BillerExperience.Api.Infrastructure.Mcp;
using Pronto.BillerExperience.Api.Infrastructure.Mcp.ServiceClients;
using Pronto.Invoice.Contracts.V1.Invoices;
using Pronto.Payment.Contracts.V1.Payments;
using Pronto.PayerAccount.Contracts.V1.Payers;
using Xunit;

namespace Pronto.BillerExperience.Api.Tests;

/// <summary>
/// Server-side authorization gates on the MCP write tools: write-capable + payer-bound tokens,
/// explicit payer confirmation, the Execution-Agent-only rule for submission, and nonprod gating
/// of seeding. Identity is always taken from the capability, never a tool argument, and the
/// idempotency key flows through to the Payment Service.
/// </summary>
public sealed class ServiceMcpToolsWriteTests
{
    private const string BillerId = "biller-1";
    private const string ExecutionAgent = "execution";

    [Fact]
    public async Task UpdatePayerPreferencesRequiresWriteAndPayerBoundCapability()
    {
        var (tools, capabilities, payers, _) = Build(seedingEnabled: false);
        payers.Payer = NewPayer("payer-9", "ACCT-1");

        // A read-only biller token cannot write, even after verification.
        var readOnlyBiller = capabilities.Issue(BillerId, "run-1", "policy", canWrite: false);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            tools.UpdatePayerPreferencesAsync(readOnlyBiller, autopay: true, paperless: null, paymentDay: 5, CancellationToken.None).AsTask());

        // A write-capable biller token that hasn't verified a payer is also rejected.
        var writeBiller = capabilities.Issue(BillerId, "run-1", "policy", canWrite: true);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            tools.UpdatePayerPreferencesAsync(writeBiller, autopay: true, paperless: null, paymentDay: 5, CancellationToken.None).AsTask());

        // Verify the payer, then the write succeeds and targets the bound payer id.
        var verified = await tools.VerifyPayerAccountAsync(writeBiller, "ACCT-1", CancellationToken.None);
        await tools.UpdatePayerPreferencesAsync(verified.PayerCapabilityToken, autopay: true, paperless: false, paymentDay: 5, CancellationToken.None);
        Assert.Equal("payer-9", payers.LastPreferencesPayerId);
    }

    [Fact]
    public async Task CreatePaymentIntentQuotesAndBindsPayerFromCapability()
    {
        var (tools, capabilities, payers, _) = Build(seedingEnabled: false);
        payers.Payer = NewPayer("payer-9", "ACCT-1");
        var token = await VerifiedToken(tools, capabilities, agentId: ExecutionAgent, canWrite: true);

        var intent = await tools.CreatePaymentIntentAsync(token, "inv-1", "card", scheduledFor: null, CancellationToken.None);

        Assert.Equal("requires_confirmation", intent.Status);
        Assert.Equal("payer-9", intent.PayerAccountId);
        Assert.Equal(1025, intent.TotalCents);
        Assert.False(string.IsNullOrWhiteSpace(intent.IntentId));
    }

    [Fact]
    public async Task SubmitPaymentIsExecutionAgentOnlyAndRequiresConfirmation()
    {
        var (tools, capabilities, payers, payments) = Build(seedingEnabled: false);
        payers.Payer = NewPayer("payer-9", "ACCT-1");

        // Non-execution agent, even write+payer-bound, cannot submit.
        var policyToken = await VerifiedToken(tools, capabilities, agentId: "policy", canWrite: true);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            tools.SubmitPaymentAsync(policyToken, "intent-1", "inv-1", "card", payerConfirmed: true, scheduledFor: null, CancellationToken.None).AsTask());

        // Execution agent without explicit confirmation is refused.
        var execToken = await VerifiedToken(tools, capabilities, agentId: ExecutionAgent, canWrite: true);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            tools.SubmitPaymentAsync(execToken, "intent-1", "inv-1", "card", payerConfirmed: false, scheduledFor: null, CancellationToken.None).AsTask());

        // Neither denied attempt reached the Payment Service.
        Assert.Null(payments.LastIdempotencyKey);

        // Execution agent with confirmation submits, forwarding the intent id as the idempotency key.
        var payment = await tools.SubmitPaymentAsync(execToken, "intent-1", "inv-1", "card", payerConfirmed: true, scheduledFor: null, CancellationToken.None);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal("intent-1", payments.LastIdempotencyKey);
        Assert.Equal("payer-9", payments.LastRequest!.PayerAccountId);
    }

    [Fact]
    public async Task SeedInvoicesIsGatedOffByDefault()
    {
        var (toolsDisabled, capabilities, _, _) = Build(seedingEnabled: false);
        var writeBiller = capabilities.Issue(BillerId, "run-1", "onboarding", canWrite: true);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            toolsDisabled.SeedInvoicesAsync(writeBiller, count: 3, accountNumber: null, billType: null, CancellationToken.None).AsTask());

        var (toolsEnabled, capabilities2, _, _) = Build(seedingEnabled: true);
        var writeBiller2 = capabilities2.Issue(BillerId, "run-1", "onboarding", canWrite: true);
        var seeded = await toolsEnabled.SeedInvoicesAsync(writeBiller2, count: 3, accountNumber: null, billType: null, CancellationToken.None);
        Assert.Equal(3, seeded.Seeded);

        // Even when enabled, a read-only capability cannot seed.
        var readOnly = capabilities2.Issue(BillerId, "run-1", "onboarding", canWrite: false);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            toolsEnabled.SeedInvoicesAsync(readOnly, count: 3, accountNumber: null, billType: null, CancellationToken.None).AsTask());
    }

    private static async Task<string> VerifiedToken(
        ServiceMcpTools tools, AgentContextCapabilityService capabilities, string agentId, bool canWrite)
    {
        var billerToken = capabilities.Issue(BillerId, "run-1", agentId, canWrite);
        var verified = await tools.VerifyPayerAccountAsync(billerToken, "ACCT-1", CancellationToken.None);
        return verified.PayerCapabilityToken;
    }

    private static PayerResponse NewPayer(string payerId, string accountNumber) =>
        new(payerId, BillerId, "Test Payer", "payer@example.com", null, [accountNumber],
            new PayerPreferences(Autopay: false, Paperless: false, Channels: [], PaymentDay: null));

    private static (ServiceMcpTools Tools, AgentContextCapabilityService Capabilities, FakePayerAccountServiceClient Payers, FakePaymentServiceClient Payments) Build(bool seedingEnabled)
    {
        var options = Options.Create(new BillerExperienceOptions
        {
            Mcp = new McpOptions
            {
                Enabled = true,
                ApiKey = new string('a', 32),
                CapabilitySigningKey = new string('s', 48),
                CapabilityLifetimeMinutes = 30,
                ExecutionAgentId = ExecutionAgent,
            },
        });
        var capabilities = new AgentContextCapabilityService(
            options, TimeProvider.System, NullLogger<AgentContextCapabilityService>.Instance);
        var payments = new FakePaymentServiceClient();
        var payers = new FakePayerAccountServiceClient();
        var tools = new ServiceMcpTools(
            capabilities,
            onboarding: null!,
            new FakeInvoiceServiceClient(),
            payments,
            payers,
            options,
            Options.Create(new MaintenanceOptions { SeedingEnabled = seedingEnabled }),
            NullLogger<ServiceMcpTools>.Instance);
        return (tools, capabilities, payers, payments);
    }

    private sealed class FakeInvoiceServiceClient : IInvoiceServiceClient
    {
        public ValueTask<InvoiceListResponse> ListAsync(string billerId, string accountNumber, bool includeClosed, CancellationToken cancellationToken)
            => ValueTask.FromResult(new InvoiceListResponse([]));
        public ValueTask<InvoiceResponse?> GetAsync(string billerId, string invoiceId, CancellationToken cancellationToken)
            => ValueTask.FromResult<InvoiceResponse?>(null);
        public ValueTask<SeedInvoicesResponse> SeedAsync(string billerId, SeedInvoicesRequest request, CancellationToken cancellationToken)
            => ValueTask.FromResult(new SeedInvoicesResponse(request.Count ?? 0, request.AccountNumber ?? "ACCT-1", []));
    }

    private sealed class FakePaymentServiceClient : IPaymentServiceClient
    {
        public string? LastIdempotencyKey { get; private set; }
        public CreatePaymentRequest? LastRequest { get; private set; }

        public ValueTask<PaymentQuoteResponse> GetQuoteAsync(string billerId, string invoiceId, string method, CancellationToken cancellationToken)
            => ValueTask.FromResult(new PaymentQuoteResponse(billerId, invoiceId, method, 1000, 25, 1025));
        public ValueTask<IReadOnlyList<PaymentResponse>> ListAsync(string billerId, string payerAccountId, CancellationToken cancellationToken)
            => ValueTask.FromResult<IReadOnlyList<PaymentResponse>>([]);

        public ValueTask<PaymentResponse> CreateAsync(CreatePaymentRequest request, string idempotencyKey, CancellationToken cancellationToken)
        {
            LastIdempotencyKey = idempotencyKey;
            LastRequest = request;
            return ValueTask.FromResult(new PaymentResponse(
                PaymentId: Guid.NewGuid().ToString(),
                BillerId: request.BillerId,
                InvoiceId: request.InvoiceId,
                PayerAccountId: request.PayerAccountId,
                Method: request.Method,
                AmountCents: 1000,
                FeeCents: 25,
                TotalCents: 1025,
                Confirmation: "PRONTO-ABC123",
                Status: PaymentStatus.Succeeded,
                ScheduledFor: request.ScheduledFor,
                ReceiptMessage: "Thanks",
                CreatedAt: DateTimeOffset.UtcNow));
        }
    }

    private sealed class FakePayerAccountServiceClient : IPayerAccountServiceClient
    {
        public PayerResponse? Payer { get; set; }
        public string? LastPreferencesPayerId { get; private set; }

        public ValueTask<PayerResponse?> FindByAccountAsync(string billerId, string accountNumber, CancellationToken cancellationToken)
            => ValueTask.FromResult(Payer);
        public ValueTask<PayerResponse?> GetAsync(string billerId, string payerId, CancellationToken cancellationToken)
            => ValueTask.FromResult(Payer);

        public ValueTask<PayerPreferences> UpdatePreferencesAsync(string billerId, string payerId, UpdatePayerPreferencesRequest request, CancellationToken cancellationToken)
        {
            LastPreferencesPayerId = payerId;
            return ValueTask.FromResult(new PayerPreferences(
                request.Autopay ?? false, request.Paperless ?? false, [], request.PaymentDay));
        }
    }
}
