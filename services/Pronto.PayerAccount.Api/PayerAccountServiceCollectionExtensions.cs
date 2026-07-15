using Pronto.PayerAccount.Api.Accounts;
using Pronto.PayerAccount.Api.Storage;
using Pronto.Persistence.Cosmos;
using Pronto.ServiceDefaults;
using Microsoft.Azure.Cosmos;

namespace Pronto.PayerAccount.Api;

/// <summary>
/// Registration seams for the Payer Account capability. A parent host can compose the whole
/// capability with <see cref="AddPayerAccountServices"/>, or wire individual concerns
/// (store, ownership verification) on its own. Kept out of <c>Program.cs</c> so the same wiring
/// is reusable without duplicating it or editing the host bootstrap.
/// </summary>
public static class PayerAccountServiceCollectionExtensions
{
    /// <summary>Default Invoice Service base address when none is configured (local dev).</summary>
    public const string DefaultInvoiceApiBaseAddress = "http://localhost:5101";

    /// <summary>Wires the store, account-ownership verification, and maintenance options.</summary>
    public static IServiceCollection AddPayerAccountServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaintenanceOptions>(configuration.GetSection(MaintenanceOptions.SectionName));
        services.AddPayerAccountStore(configuration);
        services.AddAccountOwnershipVerification(configuration);
        return services;
    }

    /// <summary>Registers the Cosmos or in-memory <see cref="IPayerStore"/> from configuration.</summary>
    public static IServiceCollection AddPayerAccountStore(
        this IServiceCollection services, IConfiguration configuration)
    {
        var persistence = configuration
            .GetSection(CosmosPersistenceOptions.SectionName)
            .Get<CosmosPersistenceOptions>() ?? new CosmosPersistenceOptions();

        if (persistence.UseCosmos)
        {
            services.AddSingleton(CosmosClientFactory.Create(persistence, "Pronto.PayerAccount.Api"));
            services.AddSingleton<IPayerStore>(provider =>
                new CosmosPayerStore(provider.GetRequiredService<CosmosClient>(), persistence.DatabaseName));
        }
        else
        {
            services.AddSingleton<IPayerStore, InMemoryPayerStore>();
        }

        return services;
    }

    /// <summary>
    /// Registers the Invoice-backed <see cref="IAccountOwnershipVerifier"/>. The base address comes
    /// from <c>Services:InvoiceApi</c> (falling back to <see cref="DefaultInvoiceApiBaseAddress"/>),
    /// and correlation headers propagate via the shared handler registered by the service defaults.
    /// </summary>
    public static IServiceCollection AddAccountOwnershipVerification(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient<IAccountOwnershipVerifier, HttpAccountOwnershipVerifier>(client =>
                client.BaseAddress = new Uri(
                    configuration["Services:InvoiceApi"] ?? DefaultInvoiceApiBaseAddress))
            .AddHttpMessageHandler<CorrelationPropagationHandler>();
        return services;
    }
}
