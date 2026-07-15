using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Pronto.Payment.Api.Tests;

/// <summary>
/// Host factory pinned to the <c>Testing</c> environment, so the in-process test
/// authentication scheme is selected explicitly rather than by relying on the default
/// environment. Production bearer validation is never engaged from tests.
/// </summary>
public sealed class TestingAppFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
    }
}
