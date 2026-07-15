using Pronto.Payment.Api.Clients;
using Pronto.Payment.Api.Purchases;
using Pronto.Payment.Api.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Pronto.Payment.Api;

/// <summary>
/// Registration surface for the recoverable purchase-completion workflow. A parent host wires
/// the whole workflow with one call; the background retry drainer is enabled only when
/// <see cref="PurchaseWorkflowOptions.BackgroundCompletionEnabled"/> is set.
/// </summary>
public static class PurchaseServiceCollectionExtensions
{
    public static IServiceCollection AddPurchaseWorkflow(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<PurchaseWorkflowOptions>()
            .Bind(configuration.GetSection(PurchaseWorkflowOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton<IBillerAccountClient, UnavailableBillerAccountClient>();
        services.TryAddSingleton<IPurchaseCompletionOutbox>(provider =>
            provider.GetRequiredService<IPurchaseStore>() as IPurchaseCompletionOutbox
            ?? throw new InvalidOperationException(
                "The configured IPurchaseStore must also provide IPurchaseCompletionOutbox, or register one explicitly."));
        services.TryAddSingleton<PurchaseCompletionRunner>();

        var options = configuration.GetSection(PurchaseWorkflowOptions.SectionName)
            .Get<PurchaseWorkflowOptions>() ?? new PurchaseWorkflowOptions();
        if (options.BackgroundCompletionEnabled)
        {
            services.AddHostedService<PurchaseCompletionProcessor>();
        }

        return services;
    }

    /// <summary>Registers a parent-supplied idempotent BillerAccount client implementation.</summary>
    public static IServiceCollection AddPurchaseWorkflow<TBillerAccountClient>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TBillerAccountClient : class, IBillerAccountClient
    {
        services.AddPurchaseWorkflow(configuration);
        services.Replace(ServiceDescriptor.Singleton<IBillerAccountClient, TBillerAccountClient>());
        return services;
    }
}
