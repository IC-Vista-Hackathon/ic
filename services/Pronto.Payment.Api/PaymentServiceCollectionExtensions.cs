using Pronto.Payment.Api.Assurance;
using Pronto.Payment.Api.Clients;
using Pronto.Payment.Api.Storage;
using Pronto.Payment.Api.Workflow;
using Pronto.Persistence.Cosmos;
using Pronto.ServiceDefaults;
using Pronto.ServiceDefaults.Security;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Pronto.Payment.Api;

/// <summary>
/// Composition surface for the Payment Service capability. A parent host wires the whole service
/// with <see cref="AddPaymentServices"/> instead of duplicating startup, and can override any piece
/// beforehand (every registration uses <c>TryAdd</c>) or tune behavior via
/// <see cref="PaymentProcessingOptions"/>. This keeps the concrete <c>Program.cs</c> to a single call.
/// </summary>
public static class PaymentServiceCollectionExtensions
{
    /// <summary>
    /// Registers stores, service clients, the recoverable payment workflow, payer-account
    /// validation, and (when enabled) the durable scheduled-payment processor.
    /// </summary>
    public static IServiceCollection AddPaymentServices(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddOptions<PaymentProcessingOptions>()
            .Bind(configuration.GetSection(PaymentProcessingOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);

        AddPaymentStores(services, configuration);

        services.TryAddSingleton<IBillerConfigClient, DemoBillerConfigClient>();

        services.AddHttpClient<IInvoiceClient, HttpInvoiceClient>(client =>
            client.BaseAddress = new Uri(
                configuration["Services:InvoiceApi"] ?? "http://localhost:5101"))
            .AddHttpMessageHandler<CorrelationPropagationHandler>()
            .AddServiceBearerToken(configuration, environment);

        AddPayerAccountValidation(services, configuration, environment);

        services.TryAddScoped<PaymentWorkflow>();
        services.TryAddScoped<ScheduledPaymentProcessor>();

        AddAssuranceServices(services, configuration);

        var processing = configuration.GetSection(PaymentProcessingOptions.SectionName)
            .Get<PaymentProcessingOptions>() ?? new PaymentProcessingOptions();
        if (processing.SchedulerEnabled)
        {
            services.AddHostedService<ScheduledPaymentWorker>();
        }

        return services;
    }

    /// <summary>
    /// Registers the post-publish assurance layer: the ledger reconciliation service, the synthetic
    /// canary runner + target source, and (when a background pass is enabled) the continuous worker.
    /// </summary>
    private static void AddAssuranceServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AssuranceOptions>().Bind(configuration.GetSection(AssuranceOptions.SectionName));
        services.AddOptions<CanaryTargetsOptions>().Bind(configuration.GetSection(CanaryTargetsOptions.SectionName));

        services.TryAddScoped<PaymentReconciliationService>();
        services.TryAddScoped<CanaryPaymentRunner>();
        services.TryAddSingleton<ICanaryTargetSource, ConfigurationCanaryTargetSource>();

        var assurance = configuration.GetSection(AssuranceOptions.SectionName)
            .Get<AssuranceOptions>() ?? new AssuranceOptions();
        if (assurance.ReconciliationEnabled || assurance.CanaryEnabled)
        {
            services.AddHostedService<AssuranceWorker>();
        }
    }

    /// <summary>
    /// Binds payer-account validation to the PayerAccount Service when <c>Services:PayerAccountApi</c>
    /// is configured, otherwise a permissive validator. Never owns PayerAccount storage.
    /// </summary>
    public static IServiceCollection AddPayerAccountValidation(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var endpoint = configuration["Services:PayerAccountApi"];
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            services.AddHttpClient<IPayerAccountValidator, HttpPayerAccountValidator>(client =>
                client.BaseAddress = new Uri(endpoint))
                .AddHttpMessageHandler<CorrelationPropagationHandler>()
                .AddServiceBearerToken(configuration, environment);
        }
        else if (environment.IsProduction())
        {
            throw new InvalidOperationException(
                "Services:PayerAccountApi is required in Production so payer_account_id ownership validation cannot be bypassed.");
        }
        else
        {
            services.TryAddSingleton<IPayerAccountValidator, PermissivePayerAccountValidator>();
        }

        return services;
    }

    private static void AddPaymentStores(IServiceCollection services, IConfiguration configuration)
    {
        var persistence = configuration.GetSection(CosmosPersistenceOptions.SectionName)
            .Get<CosmosPersistenceOptions>() ?? new CosmosPersistenceOptions();

        if (persistence.UseCosmos)
        {
            services.TryAddSingleton(CosmosClientFactory.Create(persistence, "Pronto.Payment.Api"));
            services.TryAddSingleton<IPaymentStore>(sp =>
                new CosmosPaymentStore(sp.GetRequiredService<CosmosClient>(), persistence.DatabaseName));
            services.TryAddSingleton<IPurchaseStore>(sp =>
                new CosmosPurchaseStore(sp.GetRequiredService<CosmosClient>(), persistence.DatabaseName));
        }
        else
        {
            services.TryAddSingleton<IPaymentStore, InMemoryPaymentStore>();
            services.TryAddSingleton<IPurchaseStore, InMemoryPurchaseStore>();
        }
    }
}
