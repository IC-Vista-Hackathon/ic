extern alias PayerApi;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PayerApi::Pronto.PayerAccount.Api.Accounts;

namespace Pronto.BillerExperience.IntegrationTests;

/// <summary>
/// Boots the real PayerAccount API in-process. The Invoice-backed ownership verifier is swapped for
/// an allow-all fake so the seed-then-lookup flow can run without a second live host; the seeder's
/// own behavior (deterministic payer, idempotent conflict handling) is exercised end to end.
/// </summary>
public sealed class PayerAccountAppFactory : WebApplicationFactory<PayerApi::Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAccountOwnershipVerifier>();
            services.AddSingleton<IAccountOwnershipVerifier>(new AllowAllOwnershipVerifier());
        });
    }

    private sealed class AllowAllOwnershipVerifier : IAccountOwnershipVerifier
    {
        public Task<bool> IsOwnedAsync(
            string billerId, string accountNumber, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }
}
