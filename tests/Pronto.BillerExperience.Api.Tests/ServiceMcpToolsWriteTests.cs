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
    public async Task BindExecutionCapabilityRebindsVerifiedPayerToExecutionAndPreservesBinding()
    {
        var (tools, capabilities, payers, payments) = Build(seedingEnabled: false);
        payers.Payer = NewPayer("payer-9", "ACCT-1");

        // Policy verifies the payer; that token is bound to Policy and cannot submit a payment.
        var policyPayerToken = await VerifiedToken(tools, capabilities, agentId: "policy", canWrite: true);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            tools.SubmitPaymentAsync(policyPayerToken, "intent-1", "inv-1", "card", payerConfirmed: true, scheduledFor: null, CancellationToken.None).AsTask());

        // Re-issuing at the handoff yields an Execution-bound capability that submits and keeps the payer.
        var bound = await tools.BindExecutionCapabilityAsync(policyPayerToken, CancellationToken.None);
        var payment = await tools.SubmitPaymentAsync(
            bound.ExecutionCapabilityToken, "intent-1", "inv-1", "card", payerConfirmed: true, scheduledFor: null, CancellationToken.None);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal("payer-9", payments.LastRequest!.PayerAccountId);
    }

    [Fact]
    public async Task BindExecutionCapabilityNeverExtendsTheOriginalExpiration()
    {
        // The re-issued Execution capability may narrow the lifetime but must never outlive the
        // presented one, even though it is minted later (when a fresh now+lifetime would be later).
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero));
        var (tools, capabilities, payers, _) = Build(seedingEnabled: false, timeProvider: clock);
        payers.Payer = NewPayer("payer-9", "ACCT-1");

        var policyPayerToken = await VerifiedToken(tools, capabilities, agentId: "policy", canWrite: true);
        var originalExpiry = capabilities.Validate(policyPayerToken, writeRequired: true, payerRequired: true).ExpiresAt;

        // Time passes (still inside the 30-minute lifetime) before the handoff re-issues the token.
        clock.Advance(TimeSpan.FromMinutes(10));
        var bound = await tools.BindExecutionCapabilityAsync(policyPayerToken, CancellationToken.None);
        var boundExpiry = capabilities.Validate(bound.ExecutionCapabilityToken, writeRequired: true, payerRequired: true).ExpiresAt;

        Assert.Equal(originalExpiry, boundExpiry);
        Assert.True(boundExpiry <= originalExpiry);
        // A naive re-issue would have expired at now+lifetime (10 minutes later); the cap prevented that.
        Assert.True(boundExpiry < clock.GetUtcNow().AddMinutes(30));
    }

    [Fact]
    public async Task BindExecutionCapabilityRequiresWriteCapablePayerBoundCapability()
    {
        var (tools, capabilities, payers, _) = Build(seedingEnabled: false);
        payers.Payer = NewPayer("payer-9", "ACCT-1");

        // A biller capability that never verified a payer cannot be rebound.
        var billerOnly = capabilities.Issue(BillerId, "run-1", "policy", canWrite: true);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            tools.BindExecutionCapabilityAsync(billerOnly, CancellationToken.None).AsTask());

        // A read-only payer capability cannot be rebound for the (write) payment path.
        var readOnlyPayer = await VerifiedToken(tools, capabilities, agentId: "policy", canWrite: false);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            tools.BindExecutionCapabilityAsync(readOnlyPayer, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task GuestPaymentPathCreatesAndSubmitsWithoutPayerBinding()
    {
        var (tools, capabilities, _, payments) = Build(seedingEnabled: false);
        // A guest has no payer account: Execution uses its own write-capable biller capability.
        var guest = capabilities.Issue(BillerId, "run-1", ExecutionAgent, canWrite: true);

        var intent = await tools.CreatePaymentIntentAsync(guest, "inv-1", "card", scheduledFor: null, CancellationToken.None);
        Assert.Equal("requires_confirmation", intent.Status);
        Assert.Null(intent.PayerAccountId);

        var payment = await tools.SubmitPaymentAsync(
            guest, intent.IntentId, "inv-1", "card", payerConfirmed: true, scheduledFor: null, CancellationToken.None);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Null(payments.LastRequest!.PayerAccountId);
        Assert.Equal(intent.IntentId, payments.LastIdempotencyKey);
    }

    [Fact]
    public async Task GuestSubmitStillRequiresExecutionAgentAndConfirmation()
    {
        var (tools, capabilities, _, payments) = Build(seedingEnabled: false);

        // A non-Execution biller capability cannot submit, even as a guest.
        var policyGuest = capabilities.Issue(BillerId, "run-1", "policy", canWrite: true);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            tools.SubmitPaymentAsync(policyGuest, "intent-1", "inv-1", "card", payerConfirmed: true, scheduledFor: null, CancellationToken.None).AsTask());

        // The Execution guest without explicit confirmation is refused.
        var execGuest = capabilities.Issue(BillerId, "run-1", ExecutionAgent, canWrite: true);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            tools.SubmitPaymentAsync(execGuest, "intent-1", "inv-1", "card", payerConfirmed: false, scheduledFor: null, CancellationToken.None).AsTask());

        Assert.Null(payments.LastIdempotencyKey);
    }

    [Fact]
    public async Task GuestSubmitForwardsIntentIdAsIdempotencyKeyOnRetry()
    {
        var (tools, capabilities, _, payments) = Build(seedingEnabled: false);
        var guest = capabilities.Issue(BillerId, "run-1", ExecutionAgent, canWrite: true);

        var first = await tools.SubmitPaymentAsync(
            guest, "intent-42", "inv-1", "card", payerConfirmed: true, scheduledFor: null, CancellationToken.None);
        var second = await tools.SubmitPaymentAsync(
            guest, "intent-42", "inv-1", "card", payerConfirmed: true, scheduledFor: null, CancellationToken.None);

        // The intent id is the idempotency key, so a retry resolves to the same payment downstream.
        Assert.Equal("intent-42", payments.LastIdempotencyKey);
        Assert.Equal(first.BillerId, second.BillerId);
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

    [Fact]
    public async Task RegisterPayerRequiresWriteCapableBillerCapabilityAndBindsBillerFromToken()
    {
        var (tools, capabilities, payers, _) = Build(seedingEnabled: false);

        // A read-only capability cannot register a payer.
        var readOnly = capabilities.Issue(BillerId, "run-1", "policy", canWrite: false);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            tools.RegisterPayerAsync(readOnly, "New Payer", "new@example.com", null, ["ACCT-7"],
                autopay: null, paperless: null, paymentDay: null, CancellationToken.None).AsTask());
        Assert.Null(payers.LastRegisterRequest);

        // A write-capable biller capability registers, binding the biller from the token (never an argument).
        var writeBiller = capabilities.Issue(BillerId, "run-1", "policy", canWrite: true);
        var registered = await tools.RegisterPayerAsync(
            writeBiller, "New Payer", "new@example.com", "555-0100", ["ACCT-7"],
            autopay: true, paperless: null, paymentDay: 12, CancellationToken.None);

        Assert.Equal(BillerId, payers.LastRegisterRequest!.BillerId);
        Assert.Equal("New Payer", payers.LastRegisterRequest.Name);
        Assert.Equal(["ACCT-7"], payers.LastRegisterRequest.AccountNumbers);
        Assert.NotNull(payers.LastRegisterRequest.Preferences);
        Assert.True(payers.LastRegisterRequest.Preferences!.Autopay);
        Assert.Equal(12, payers.LastRegisterRequest.Preferences.PaymentDay);
        Assert.Equal("payer-new", registered.PayerId);
    }

    [Fact]
    public async Task RegisterPayerLeavesPreferencesNullWhenNoPreferenceFieldsSupplied()
    {
        var (tools, capabilities, payers, _) = Build(seedingEnabled: false);
        var writeBiller = capabilities.Issue(BillerId, "run-1", "policy", canWrite: true);

        await tools.RegisterPayerAsync(
            writeBiller, "New Payer", "new@example.com", null, ["ACCT-7"],
            autopay: null, paperless: null, paymentDay: null, CancellationToken.None);

        Assert.Null(payers.LastRegisterRequest!.Preferences);
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

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private static (ServiceMcpTools Tools, AgentContextCapabilityService Capabilities, FakePayerAccountServiceClient Payers, FakePaymentServiceClient Payments) Build(bool seedingEnabled, TimeProvider? timeProvider = null)
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
            options, timeProvider ?? TimeProvider.System, NullLogger<AgentContextCapabilityService>.Instance);
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
        public RegisterPayerRequest? LastRegisterRequest { get; private set; }

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

        public ValueTask<PayerResponse> RegisterAsync(RegisterPayerRequest request, CancellationToken cancellationToken)
        {
            LastRegisterRequest = request;
            return ValueTask.FromResult(new PayerResponse(
                PayerId: "payer-new",
                BillerId: request.BillerId,
                Name: request.Name,
                Email: request.Email,
                Phone: request.Phone,
                AccountNumbers: request.AccountNumbers,
                Preferences: request.Preferences
                    ?? new PayerPreferences(Autopay: false, Paperless: false, Channels: [], PaymentDay: null)));
        }
    }
}
