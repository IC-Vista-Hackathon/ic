namespace Pronto.ServiceDefaults.Security;

/// <summary>
/// Authentication configuration for a Pronto service host, bound from the
/// <c>Authentication</c> section (env <c>Authentication__Authority</c>,
/// <c>Authentication__Audience</c>, …).
///
/// Production hosts authenticate callers (agents via the AI Foundry tool registry, and
/// peer services) with a JWT bearer token issued by Microsoft Entra. Local and test hosts
/// use an explicit in-process test scheme instead — see <see cref="ServiceAuthentication"/>.
/// The choice is driven by the hosting environment, never by the presence or absence of a
/// token, so production can never silently fall back to an unauthenticated mode.
/// </summary>
public sealed class ServiceAuthenticationOptions
{
    public const string SectionName = "Authentication";

    /// <summary>
    /// OIDC authority (Entra tenant/authority) that issues and signs caller tokens.
    /// Required whenever production bearer authentication is active; a host refuses to
    /// start without it (fail-closed) rather than serving traffic with no identity provider.
    /// </summary>
    public string? Authority { get; set; }

    /// <summary>Expected token audience (this service's API identifier / app ID URI).</summary>
    public string? Audience { get; set; }

    /// <summary>
    /// Extra accepted audiences (e.g. the app registration's client id alongside its URI).
    /// </summary>
    public IList<string> ValidAudiences { get; } = new List<string>();

    /// <summary>
    /// Force production JWT bearer authentication even outside the Production environment.
    /// Lets a nonprod/staging deployment opt into real tokens; it can never <em>disable</em>
    /// production auth. The test scheme is only ever selected in Development/Testing when this
    /// is false.
    /// </summary>
    public bool RequireBearerToken { get; set; }
}
