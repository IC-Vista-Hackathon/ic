using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pronto.Agentic.Orchestration.Abstractions;
using Pronto.BillerExperience.Api;
using Pronto.BillerExperience.Api.Application;
using Pronto.BillerExperience.Api.Configuration;
using Pronto.BillerExperience.Api.Infrastructure.AI;
using Pronto.BillerExperience.Api.Infrastructure.Mcp;
using Pronto.BillerExperience.Api.Infrastructure.Mcp.ServiceClients;
using Pronto.BillerExperience.Api.Infrastructure.Persistence;
using Pronto.Invoice.Contracts.V1.Invoices;
using Pronto.Payment.Contracts.V1.Payments;
using Pronto.PayerAccount.Contracts.V1.Payers;
using Xunit;

namespace Pronto.BillerExperience.Api.Tests;

public sealed class McpServiceRouterTests
{
    private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) },
    };

    [Fact]
    public void ToolRegistryAndResultContractsUseSnakeCaseWireShapes()
    {
        var registry = new ServiceToolRegistry();

        Assert.All(registry.All, descriptor =>
        {
            Assert.Matches("^[a-z][a-z0-9]*(?:_[a-z0-9]+)*$", descriptor.Name);
            Assert.DoesNotContain(' ', descriptor.Name);
        });

        var descriptorJson = JsonSerializer.Serialize(
            registry.Get(ServiceToolRegistry.ToolNames.GetPayerProfile), WireOptions);
        Assert.Contains("\"scope\":\"payer\"", descriptorJson);
        Assert.Contains("\"write_capability_required\":false", descriptorJson);

        var resultJson = JsonSerializer.Serialize(
            new PayerVerificationResult("payer-1", "Private Name", "private-token"), WireOptions);
        Assert.Contains("\"payer_id\"", resultJson);
        Assert.Contains("\"payer_capability_token\"", resultJson);
        Assert.DoesNotContain("\"PayerId\"", resultJson);
    }

    [Fact]
    public async Task ReadToolsRejectMissingExpiredAndUnboundCapabilities()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero));
        var (tools, capabilities, _) = CreateTools(time);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            tools.GetInvoiceAsync("", "invoice-1", default).AsTask());

        var expired = capabilities.Issue("biller-1", "run-1", "agent-1", canWrite: false);
        time.Advance(TimeSpan.FromMinutes(11));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            tools.ListInvoicesAsync(expired, "account-1", includeClosed: false, default).AsTask());

        var billerScoped = capabilities.Issue("biller-1", "run-1", "agent-1", canWrite: false);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            tools.GetPayerProfileAsync(billerScoped, default).AsTask());
    }

    [Fact]
    public async Task CapabilityDenialEmitsPrivacySafeFailedTelemetry()
    {
        var (tools, _, _) = CreateTools();
        using var request = new Activity("mcp-request").Start();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            tools.GetPaymentHistoryAsync("invalid-token", default).AsTask());

        var failed = Assert.Single(request.Events, item => item.Name == "mcp.tool_failed");
        var fields = failed.Tags.ToDictionary(item => item.Key, item => item.Value);
        Assert.Equal(ServiceToolRegistry.ToolNames.GetPaymentHistory, fields["tool_name"]);
        Assert.Equal("unauthorized", fields["failure_category"]);
        Assert.Equal(403, fields["status_code"]);
        AssertPrivacyAllowlist(fields.Keys);
    }

    [Fact]
    public async Task VerifyThenReadEmitsAllowlistedInvocationTelemetry()
    {
        var (tools, capabilities, payerClient) = CreateTools();
        payerClient.Payer = new PayerResponse(
            "payer-1",
            "biller-1",
            "Private Name",
            "private@example.test",
            "555-0100",
            ["private-account"],
            new PayerPreferences(false, true, [NotificationChannel.Email], null));
        var activities = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Pronto.BillerExperience",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => activities.Add(activity),
        };
        ActivitySource.AddActivityListener(listener);
        // Unique run id isolates this test's activities from any concurrently-running
        // tests: the listener is process-global, so activities from parallel tests using
        // the same tool names would otherwise leak into the collected bag.
        var runId = $"run-{Guid.NewGuid():N}";
        var token = capabilities.Issue("biller-1", runId, "agent-1", canWrite: false);

        var verification = await tools.VerifyPayerAccountAsync(token, "private-account", default);
        var profile = await tools.GetPayerProfileAsync(verification.PayerCapabilityToken, default);

        Assert.Equal("payer-1", profile.PayerId);
        string[] expectedTools =
        [
            ServiceToolRegistry.ToolNames.VerifyPayerAccount,
            ServiceToolRegistry.ToolNames.GetPayerProfile,
        ];
        var toolActivities = activities
            .Where(activity => (activity.GetTagItem("run_id") as string) == runId
                && expectedTools.Contains(activity.GetTagItem("tool_name")))
            .ToList();
        Assert.Equal(2, toolActivities.Count);
        Assert.All(toolActivities, activity =>
        {
            Assert.Contains(activity.Events, item => item.Name == "mcp.tool_invoked");
            Assert.Contains(activity.Events, item => item.Name == "mcp.tool_completed");
            Assert.DoesNotContain(activity.Events, item => item.Name == "mcp.tool_failed");
            Assert.All(activity.Events, telemetryEvent =>
            {
                AssertPrivacyAllowlist(telemetryEvent.Tags.Select(item => item.Key));
                var serialized = string.Join('|', telemetryEvent.Tags.Select(item => item.Value?.ToString()));
                Assert.DoesNotContain("Private Name", serialized);
                Assert.DoesNotContain("private@example.test", serialized);
                Assert.DoesNotContain("private-account", serialized);
                Assert.DoesNotContain(verification.PayerCapabilityToken, serialized);
            });
        });
    }

    [Fact]
    public async Task DownstreamFailureEmitsFailedMetricAndEvent()
    {
        var (tools, capabilities, _) = CreateTools();
        var measurements = new ConcurrentBag<(long Value, KeyValuePair<string, object?>[] Tags)>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "Pronto.BillerExperience" &&
                instrument.Name == "ic.mcp.tool.failed")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            measurements.Add((value, tags.ToArray())));
        meterListener.Start();
        var activities = new ConcurrentBag<Activity>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Pronto.BillerExperience",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => activities.Add(activity),
        };
        ActivitySource.AddActivityListener(activityListener);
        var token = capabilities.Issue("biller-1", "run-1", "agent-1", canWrite: false);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            tools.GetInvoiceAsync(token, "missing-invoice", default).AsTask());

        var failedActivity = Assert.Single(activities, activity =>
            Equals(activity.GetTagItem("tool_name"), ServiceToolRegistry.ToolNames.GetInvoice));
        var failedEvent = Assert.Single(failedActivity.Events, item => item.Name == "mcp.tool_failed");
        Assert.Equal("not_found", failedEvent.Tags.Single(item => item.Key == "failure_category").Value);
        var measurement = Assert.Single(measurements, item => item.Tags.Any(tag =>
            tag.Key == "tool" && Equals(tag.Value, ServiceToolRegistry.ToolNames.GetInvoice)));
        Assert.Equal(1, measurement.Value);
        Assert.Contains(measurement.Tags, item =>
            item.Key == "failure_category" && Equals(item.Value, "not_found"));
    }

    private static void AssertPrivacyAllowlist(IEnumerable<string> keys)
    {
        string[] allowed =
        [
            "tool_name",
            "biller_id",
            "agent_id",
            "run_id",
            "write_capable",
            "payer_bound",
            "outcome",
            "failure_category",
            "status_code",
            "duration_ms",
            "trace_id",
        ];
        Assert.All(keys, key => Assert.Contains(key, allowed));
    }

    private static (ServiceMcpTools Tools, AgentContextCapabilityService Capabilities, FakePayerClient PayerClient)
        CreateTools(TimeProvider? timeProvider = null)
    {
        var options = Options.Create(new BillerExperienceOptions
        {
            Mcp = new McpOptions
            {
                Enabled = true,
                ApiKey = new string('a', 32),
                CapabilitySigningKey = new string('s', 48),
                CapabilityLifetimeMinutes = 10,
            },
        });
        var capabilities = new AgentContextCapabilityService(
            options, timeProvider ?? TimeProvider.System, NullLogger<AgentContextCapabilityService>.Instance);
        var payerClient = new FakePayerClient();
        var services = new ServiceCollection();
        services.AddSingleton(options);
        services.AddSingleton<IOptions<MaintenanceOptions>>(Options.Create(new MaintenanceOptions()));
        services.AddSingleton(capabilities);
        services.AddSingleton(
            new BillerOnboardingService(
                new InMemoryBillerExperienceRepository(),
                new UnusedDraftGenerator(),
                new UnusedOrchestrationRunner(),
                NullLogger<BillerOnboardingService>.Instance));
        services.AddSingleton<IInvoiceServiceClient, FakeInvoiceClient>();
        services.AddSingleton<IPaymentServiceClient, FakePaymentClient>();
        services.AddSingleton<IPayerAccountServiceClient>(payerClient);
        services.AddSingleton<ILogger<ServiceMcpTools>>(NullLogger<ServiceMcpTools>.Instance);
        using var provider = services.BuildServiceProvider();
        var tools = ActivatorUtilities.CreateInstance<ServiceMcpTools>(provider);
        return (tools, capabilities, payerClient);
    }

    private sealed class FakeInvoiceClient : IInvoiceServiceClient
    {
        public SeedInvoicesRequest? LastSeedRequest { get; private set; }

        public ValueTask<InvoiceListResponse> ListAsync(
            string billerId, string accountNumber, bool includeClosed, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new InvoiceListResponse([]));

        public ValueTask<InvoiceResponse?> GetAsync(
            string billerId, string invoiceId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<InvoiceResponse?>(null);

        public ValueTask<SeedInvoicesResponse> SeedAsync(
            string billerId, SeedInvoicesRequest request, CancellationToken cancellationToken)
        {
            LastSeedRequest = request;
            return ValueTask.FromResult(new SeedInvoicesResponse(0, request.AccountNumber ?? "account-1", []));
        }
    }

    private sealed class FakePaymentClient : IPaymentServiceClient
    {
        public ValueTask<PaymentQuoteResponse> GetQuoteAsync(
            string billerId, string invoiceId, string method, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<IReadOnlyList<PaymentResponse>> ListAsync(
            string billerId, string payerAccountId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<PaymentResponse>>([]);

        public ValueTask<PaymentResponse> CreateAsync(
            CreatePaymentRequest request, string idempotencyKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakePayerClient : IPayerAccountServiceClient
    {
        public PayerResponse? Payer { get; set; }

        public ValueTask<PayerResponse?> FindByAccountAsync(
            string billerId, string accountNumber, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Payer);

        public ValueTask<PayerResponse?> GetAsync(
            string billerId, string payerId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Payer);

        public ValueTask<PayerPreferences> UpdatePreferencesAsync(
            string billerId,
            string payerId,
            UpdatePayerPreferencesRequest request,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(Payer?.Preferences ?? new PayerPreferences(false, false, [], null));
    }

    private sealed class UnusedDraftGenerator : IExperienceDraftGenerator
    {
        public string Provider => "unused";

        public ValueTask<DraftGenerationResult> GenerateAsync(
            Api.Domain.BillerRecord biller,
            Api.Domain.ExperienceRecord current,
            IReadOnlyList<Contracts.V1.Onboarding.OnboardingChatMessage> messages,
            Contracts.V1.Research.BillerResearchResponse research,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedOrchestrationRunner : IOrchestrationRunner
    {
        public ValueTask<TOutput> RunAsync<TInput, TOutput>(
            IOrchestrationWorkflow<TInput, TOutput> workflow,
            TInput input,
            OrchestrationContext context,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan duration) => current += duration;
    }
}
