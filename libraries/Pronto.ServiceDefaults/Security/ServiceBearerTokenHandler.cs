using System.Net.Http.Headers;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Pronto.ServiceDefaults.Security;

public sealed class ServiceBearerTokenHandler(
    TokenCredential credential,
    string scope) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = await credential.GetTokenAsync(
            new TokenRequestContext([scope]),
            cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        return await base.SendAsync(request, cancellationToken);
    }
}

public static class ServiceBearerTokenExtensions
{
    public static IHttpClientBuilder AddServiceBearerToken(
        this IHttpClientBuilder builder,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var scope = configuration["Authentication:ServiceScope"];
        if (string.IsNullOrWhiteSpace(scope))
        {
            if (environment.IsProduction())
            {
                throw new InvalidOperationException(
                    "Authentication:ServiceScope is required in Production for authenticated service-to-service calls.");
            }

            return builder;
        }

        builder.Services.TryAddSingleton<TokenCredential>(_ => new DefaultAzureCredential());
        return builder.AddHttpMessageHandler(services =>
            new ServiceBearerTokenHandler(
                services.GetRequiredService<TokenCredential>(),
                scope));
    }
}
