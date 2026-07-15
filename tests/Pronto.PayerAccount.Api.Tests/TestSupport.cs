using Pronto.PayerAccount.Api.Accounts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Pronto.PayerAccount.Api.Tests;

/// <summary>
/// In-process host with the Invoice-backed ownership verifier swapped for a fake, so account
/// linking can be exercised without a running Invoice Service. Ownership defaults to allow-all;
/// set <see cref="Ownership"/> before the first request to change the policy.
/// </summary>
public sealed class PayerAccountApiFactory : WebApplicationFactory<Program>
{
    public Func<string, string, bool> Ownership { get; set; } = (_, _) => true;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAccountOwnershipVerifier>();
            services.AddSingleton<IAccountOwnershipVerifier>(
                new FakeAccountOwnershipVerifier((biller, account) => Ownership(biller, account)));
        });
    }
}

public sealed class FakeAccountOwnershipVerifier : IAccountOwnershipVerifier
{
    private readonly Func<string, string, bool> allow;

    public FakeAccountOwnershipVerifier(Func<string, string, bool> allow) => this.allow = allow;

    public Task<bool> IsOwnedAsync(
        string billerId, string accountNumber, CancellationToken cancellationToken = default)
        => Task.FromResult(allow(billerId, accountNumber));
}
