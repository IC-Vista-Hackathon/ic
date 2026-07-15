using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Pronto.ServiceDefaults.Security;

/// <summary>
/// In-process authentication for the Development and Testing environments only. It is never
/// registered in Production (see <see cref="ServiceAuthentication"/>), so it is an explicit
/// test affordance, not a production bypass.
///
/// It authenticates every request as a full-access service principal so local runs and existing
/// functional tests work without minting real tokens. Tests exercising tenant/role enforcement
/// narrow the identity per request via headers:
/// <list type="bullet">
///   <item><c>X-Test-Subject</c> — the caller's subject id.</item>
///   <item><c>X-Test-Roles</c> — space-delimited app roles (overrides the full-access default).</item>
///   <item><c>X-Test-Biller-Id</c> — the single biller the caller is scoped to.</item>
/// </list>
/// </summary>
public sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";
    public const string SubjectHeader = "X-Test-Subject";
    public const string RolesHeader = "X-Test-Roles";
    public const string BillerHeader = "X-Test-Biller-Id";

    public TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var subject = Request.Headers[SubjectHeader].FirstOrDefault();
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, string.IsNullOrWhiteSpace(subject) ? "test-service" : subject),
        };

        // Header absent → full access; header present (even empty) → exactly the roles given,
        // so a test can assert the "no matching role" path.
        var roles = Request.Headers.TryGetValue(RolesHeader, out var rolesValues)
            ? rolesValues.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : ServiceClaims.AllRoles;
        foreach (var role in roles)
        {
            claims.Add(new Claim("roles", role));
        }

        var billerId = Request.Headers[BillerHeader].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(billerId))
        {
            claims.Add(new Claim(ServiceClaims.BillerId, billerId.Trim()));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
