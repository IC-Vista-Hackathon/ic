using IC.Payment.Contracts.V1.Purchases;

namespace IC.Payment.Api.Clients;

/// <summary>
/// Cross-service write: advance BillerAccount.status to purchased (and tier to the plan)
/// after a Purchase is paid. Design/entities.md documents this handoff.
/// </summary>
public interface IBillerAccountClient
{
    Task AdvanceToPurchasedAsync(string billerId, PurchasePlan plan, CancellationToken cancellationToken);
}

/// <summary>
/// No-op stub: BillerExperience.Api has no account-status endpoint yet. Logs the intent so
/// the demo trace shows where the real call goes.
/// </summary>
public sealed partial class NoOpBillerAccountClient : IBillerAccountClient
{
    private readonly ILogger<NoOpBillerAccountClient> logger;

    public NoOpBillerAccountClient(ILogger<NoOpBillerAccountClient> logger)
    {
        this.logger = logger;
    }

    public Task AdvanceToPurchasedAsync(string billerId, PurchasePlan plan, CancellationToken cancellationToken)
    {
        LogWouldAdvance(logger, billerId, plan);
        return Task.CompletedTask;
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Would advance BillerAccount {BillerId} to purchased (tier {Plan}) — endpoint pending on BillerExperience.Api")]
    private static partial void LogWouldAdvance(ILogger logger, string billerId, PurchasePlan plan);
}
