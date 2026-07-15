using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Pronto.ServiceDefaults.Security;

/// <summary>
/// Fail-closed authentication and authorization shared by every Pronto service host.
///
/// The scheme is chosen by hosting environment, not by request content:
/// <list type="bullet">
///   <item><b>Production</b> (or any environment with <c>Authentication:RequireBearerToken</c>)
///   validates JWT bearer tokens from the configured Entra authority. A missing authority is a
///   startup failure — the host will not serve traffic without an identity provider.</item>
///   <item><b>Development / Testing</b> uses the in-process <see cref="TestAuthenticationHandler"/>.</item>
/// </list>
///
/// Authorization is fail-closed via a fallback policy that requires an authenticated caller on
/// every endpoint; endpoints opt out explicitly with <c>AllowAnonymous</c> (health probes, root).
/// </summary>
public static class ServiceAuthentication
{
    public static WebApplicationBuilder AddServiceAuthentication(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(ServiceAuthenticationOptions.SectionName)
            .Get<ServiceAuthenticationOptions>() ?? new ServiceAuthenticationOptions();

        var useBearer = builder.Environment.IsProduction() || options.RequireBearerToken;

        if (useBearer)
        {
            if (string.IsNullOrWhiteSpace(options.Authority))
            {
                throw new InvalidOperationException(
                    "Authentication:Authority must be configured when bearer authentication is active " +
                    "(Production, or Authentication:RequireBearerToken=true). Refusing to start without an " +
                    "identity provider — fail closed.");
            }

            builder.Services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(jwt =>
                {
                    jwt.Authority = options.Authority;
                    jwt.TokenValidationParameters.ValidateIssuer = true;
                    jwt.TokenValidationParameters.ValidateAudience = true;
                    jwt.TokenValidationParameters.ValidateLifetime = true;
                    if (!string.IsNullOrWhiteSpace(options.Audience))
                    {
                        jwt.Audience = options.Audience;
                    }

                    if (options.ValidAudiences.Count > 0)
                    {
                        jwt.TokenValidationParameters.ValidAudiences = options.ValidAudiences;
                    }
                });
        }
        else
        {
            builder.Services
                .AddAuthentication(TestAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName, _ => { });
        }

        builder.Services.AddAuthorization(authorization =>
        {
            authorization.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
            authorization.AddServicePolicies();
        });

        return builder;
    }
}
